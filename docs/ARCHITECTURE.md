# Arquitetura do ZYRON Print

## Aplicativo Windows

- WinForms em .NET 8, publicado como `win-x64`.
- Uma única instância por usuário do Windows.
- Impressão RAW pelo spooler do Windows, sem diálogo de confirmação.
- ESC/POS em CP850, com 32 colunas para 58 mm e 48 para 80 mm.
- Configurações em `%LocalAppData%\ZYRON\Print\settings.json`.
- Credencial individual protegida pelo DPAPI do usuário atual em `device.dat`.
- Logs locais diários, mantidos por 14 dias.
- Inicialização pelo registro do usuário e operação na bandeja.
- Worker com polling curto; Realtime pode ser adicionado depois apenas como acelerador.

## Fluxo seguro

1. Um gerente gera no painel um código aleatório, de uso único e curta duração.
2. O banco armazena somente o hash do código.
3. A Edge Function `zyron-print-pair` valida o código, cria uma identidade Supabase Auth exclusiva e liga `auth.uid()` a um único `restaurant_id`.
4. A `service_role` existe somente nessa Edge Function e nunca é enviada ao computador.
5. O aplicativo guarda o token retornado com DPAPI.
6. O dispositivo não possui `SELECT` direto em `print_jobs`; chama somente RPCs protegidas.
7. `claim_print_job()` descobre a loja pelo dispositivo autenticado e usa `FOR UPDATE SKIP LOCKED`.
8. Conclusão e falha exigem a mesma loja, o mesmo dispositivo e um job em `printing`.
9. A unicidade da primeira via e a chave de deduplicação impedem duplicatas; reimpressões são novos jobs auditáveis.

## Atualização futura

Separar o agente, protocolo da API e versão do payload permite adicionar um atualizador assinado posteriormente. O instalador deve instalar por usuário e pode ser trocado por MSIX ou um bootstrapper com assinatura de código sem alterar o núcleo de impressão.

