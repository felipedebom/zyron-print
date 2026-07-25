# Etapas verificáveis

1. **Base local:** abrir o app, listar impressoras, salvar seleção, imprimir teste e validar acentos/corte.
2. **Operação Windows:** validar bandeja, início minimizado, inicialização no login e logs.
3. **Segurança no Supabase:** revisar/aplicar a migração, criar a Edge Function de pareamento e testar isolamento com duas lojas.
4. **Fila real:** adaptar a geração do payload/versionamento, validar lock, falha, retomada e deduplicação.
5. **Transição:** somente com autorização, adicionar ao Delivery a geração do código e a escolha temporária entre QZ Tray e ZYRON Print.
6. **Distribuição:** gerar instalador, assinar binários e executar teste limpo em Windows 10/11 com XP-58, 58 mm e 80 mm.
7. **Atualização:** adicionar manifesto assinado, canal de versão e rollback.

Cada etapa deve encerrar com build, testes automatizados e uma lista curta de testes manuais realizados.

