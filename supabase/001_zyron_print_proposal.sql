-- PROPOSTA PARA REVISÃO. NÃO APLICADA AO ZYRON DELIVERY.
-- A Edge Function de pareamento é a única parte que usa service_role, somente no servidor.

create extension if not exists pgcrypto;

create type public.print_device_status as enum ('offline', 'connected', 'printing', 'error', 'revoked');

create table public.print_pairing_codes (
  id uuid primary key default gen_random_uuid(),
  restaurant_id uuid not null references public.restaurants(id) on delete cascade,
  code_hash bytea not null,
  created_by uuid not null references auth.users(id),
  expires_at timestamptz not null,
  used_at timestamptz,
  used_by_device_id uuid,
  created_at timestamptz not null default now(),
  constraint print_pairing_code_expiry_valid check (expires_at > created_at)
);

create table public.print_devices (
  id uuid primary key default gen_random_uuid(),
  restaurant_id uuid not null references public.restaurants(id) on delete cascade,
  auth_user_id uuid not null unique references auth.users(id) on delete cascade,
  name text not null,
  platform text not null default 'windows',
  app_version text,
  printer_name text,
  status public.print_device_status not null default 'offline',
  last_seen_at timestamptz,
  revoked_at timestamptz,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

alter table public.print_pairing_codes
  add constraint print_pairing_codes_used_device_fkey
  foreign key (used_by_device_id) references public.print_devices(id);

alter table public.print_jobs
  add column if not exists claimed_by_device_id uuid references public.print_devices(id),
  add column if not exists next_attempt_at timestamptz not null default now(),
  add column if not exists completed_by_device_id uuid references public.print_devices(id),
  add column if not exists deduplication_key text,
  add column if not exists cut boolean not null default true;

create unique index if not exists print_jobs_deduplication_unique
  on public.print_jobs (restaurant_id, deduplication_key)
  where deduplication_key is not null;

create index if not exists print_jobs_device_queue_idx
  on public.print_jobs (restaurant_id, status, next_attempt_at, created_at);

alter table public.print_pairing_codes enable row level security;
alter table public.print_devices enable row level security;

-- Administradores/gestores da própria loja podem gerar códigos e ver dispositivos.
create policy print_pairing_codes_manager_access
on public.print_pairing_codes
for all to authenticated
using (
  public.is_platform_admin()
  or exists (
    select 1 from public.restaurant_members rm
    where rm.restaurant_id = print_pairing_codes.restaurant_id
      and rm.user_id = auth.uid()
      and rm.active
      and rm.role in ('owner', 'administrator', 'manager')
  )
)
with check (
  public.is_platform_admin()
  or exists (
    select 1 from public.restaurant_members rm
    where rm.restaurant_id = print_pairing_codes.restaurant_id
      and rm.user_id = auth.uid()
      and rm.active
      and rm.role in ('owner', 'administrator', 'manager')
  )
);

create policy print_devices_manager_read
on public.print_devices
for select to authenticated
using (
  public.is_platform_admin()
  or exists (
    select 1 from public.restaurant_members rm
    where rm.restaurant_id = print_devices.restaurant_id
      and rm.user_id = auth.uid()
      and rm.active
      and rm.role in ('owner', 'administrator', 'manager')
  )
);

-- O dispositivo enxerga apenas seu próprio registro. Ele não lê a tabela de jobs diretamente.
create policy print_devices_self_read
on public.print_devices
for select to authenticated
using (auth_user_id = auth.uid() and revoked_at is null);

revoke all on public.print_jobs from anon, authenticated;
revoke all on public.print_pairing_codes from anon;
revoke all on public.print_devices from anon;
grant select on public.print_devices to authenticated;

create or replace function public.current_print_device()
returns public.print_devices
language sql
stable
security definer
set search_path = public
as $$
  select d.*
  from public.print_devices d
  where d.auth_user_id = auth.uid()
    and d.revoked_at is null
  limit 1
$$;

revoke all on function public.current_print_device() from public;
grant execute on function public.current_print_device() to authenticated;

create or replace function public.claim_print_job()
returns setof public.print_jobs
language plpgsql
security definer
set search_path = public
as $$
declare
  v_device public.print_devices;
  v_job public.print_jobs;
begin
  select * into v_device from public.current_print_device();
  if v_device.id is null then
    raise exception 'print_device_not_authorized' using errcode = '42501';
  end if;

  select j.* into v_job
  from public.print_jobs j
  where j.restaurant_id = v_device.restaurant_id
    and j.status in ('pending', 'failed')
    and j.next_attempt_at <= now()
    and (j.locked_at is null or j.locked_at < now() - interval '2 minutes')
  order by j.created_at
  for update skip locked
  limit 1;

  if v_job.id is null then return; end if;

  update public.print_jobs
  set status = 'printing',
      attempts = attempts + 1,
      locked_at = now(),
      locked_by = v_device.id::text,
      claimed_by_device_id = v_device.id,
      last_error = null,
      updated_at = now()
  where id = v_job.id
  returning * into v_job;

  update public.print_devices
  set status = 'printing', last_seen_at = now(), updated_at = now()
  where id = v_device.id;

  return next v_job;
end;
$$;

create or replace function public.complete_print_job(p_job_id uuid)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare v_device public.print_devices;
begin
  select * into v_device from public.current_print_device();
  if v_device.id is null then raise exception 'print_device_not_authorized' using errcode = '42501'; end if;

  update public.print_jobs
  set status = 'printed', printed_at = now(), completed_by_device_id = v_device.id,
      locked_at = null, locked_by = null, last_error = null, updated_at = now()
  where id = p_job_id
    and restaurant_id = v_device.restaurant_id
    and claimed_by_device_id = v_device.id
    and status = 'printing';
  if not found then raise exception 'print_job_not_owned' using errcode = '42501'; end if;
end;
$$;

create or replace function public.fail_print_job(p_job_id uuid, p_error text, p_retry_seconds integer default 30)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare v_device public.print_devices;
begin
  select * into v_device from public.current_print_device();
  if v_device.id is null then raise exception 'print_device_not_authorized' using errcode = '42501'; end if;

  update public.print_jobs
  set status = 'failed', last_error = left(coalesce(p_error, 'Falha ao imprimir'), 500),
      next_attempt_at = now() + make_interval(secs => greatest(10, least(p_retry_seconds, 3600))),
      locked_at = null, locked_by = null, updated_at = now()
  where id = p_job_id
    and restaurant_id = v_device.restaurant_id
    and claimed_by_device_id = v_device.id
    and status = 'printing';
  if not found then raise exception 'print_job_not_owned' using errcode = '42501'; end if;
end;
$$;

create or replace function public.heartbeat_print_device(
  p_printer_name text,
  p_status public.print_device_status,
  p_app_version text
)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  update public.print_devices
  set printer_name = left(p_printer_name, 200),
      status = p_status,
      app_version = left(p_app_version, 50),
      last_seen_at = now(),
      updated_at = now()
  where auth_user_id = auth.uid() and revoked_at is null;
  if not found then raise exception 'print_device_not_authorized' using errcode = '42501'; end if;
end;
$$;

revoke all on function public.claim_print_job() from public;
revoke all on function public.complete_print_job(uuid) from public;
revoke all on function public.fail_print_job(uuid, text, integer) from public;
revoke all on function public.heartbeat_print_device(text, public.print_device_status, text) from public;
grant execute on function public.claim_print_job() to authenticated;
grant execute on function public.complete_print_job(uuid) to authenticated;
grant execute on function public.fail_print_job(uuid, text, integer) to authenticated;
grant execute on function public.heartbeat_print_device(text, public.print_device_status, text) to authenticated;

-- Reimpressão autorizada: o painel cria um novo print_job com is_reprint=true,
-- deduplication_key única (ex.: order_id || ':reprint:' || request_uuid) e created_by auditável.
-- Nunca se reabre a primeira via já concluída.

