# ZYRON Print

Aplicativo Windows para impressão automática de comandas do ZYRON Delivery, sem caixas de confirmação.

## Executar

Requer Windows 10/11 e .NET SDK 8 para desenvolvimento:

```powershell
dotnet build .\Zyron.Print.sln
dotnet test .\Zyron.Print.sln
dotnet run --project .\src\Zyron.Print\Zyron.Print.csproj
```

Para gerar uma versão independente do .NET instalado:

```powershell
dotnet publish .\src\Zyron.Print\Zyron.Print.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\artifacts\publish
```

O arquivo `installer\ZYRON Print.iss` gera o instalador com Inno Setup 6.

## Configuração simples

1. Instale no Windows o driver da impressora térmica.
2. Abra o ZYRON Print e escolha a impressora exibida pelo Windows.
3. Escolha 58 mm ou 80 mm e clique em **Imprimir teste**.
4. Gere no painel da loja um código temporário e use **Parear computador**.
5. Minimize o aplicativo; ele continuará perto do relógio.

O desenho inicial permanece documentado em `supabase\001_zyron_print_proposal.sql`. A implementação oficial está no projeto ZYRON Delivery, na migração `20260725213000_zyron_print_devices.sql`, e usa a Edge Function `zyron-print-pair`.

## Estado atual

A versão 0.1 implementa a base Windows, impressão RAW/ESC-POS, CP850, bandeja, autostart, configurações, logs, pareamento seguro, renovação automática da sessão e worker da fila. A partir da versão 0.1.8, o cabeçalho publicitário monocromático da ZYRON é gerado em raster dentro do aplicativo, funciona offline e respeita a opção configurada pela loja. O Delivery mantém o QZ Tray como modo de compatibilidade e permite selecionar o ZYRON Print por loja.
