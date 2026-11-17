# Página de teste do embed

Um "CRM Exemplo" que mostra o embed de templates do Contratos, do jeito que um cliente integraria:
o servidor (`server.js`) guarda a chave de API e cria as sessões; a página só recebe o link e coloca no iframe.

Dá para testar:
- permissões (`edit`, `publish`, `archive` ou só leitura) e abrir direto num template;
- os avisos da página (`ready`, `template.saved`, `template.published`, `session.expired`...), no painel "Avisos do embed";
- a lista de templates publicados (API pública), que se atualiza quando o embed publica ou arquiva;
- encerrar a sessão pelo "CRM" e a altura automática pelo evento `resize`.

## Como usar

1. No Contratos, em **Integrações**:
   - crie uma chave de API;
   - em **Embed de templates**, cadastre `http://localhost:3001` (ou a porta que você usar).
2. Crie `tools/embed-demo/.env.local`. Ele fica fora do git.

   ```
   CONTRATOS_URL=http://localhost:7700
   CONTRATOS_API_KEY=ctr_...
   PORT=3001
   ```

3. Suba a página e abra `http://localhost:3001`:

   ```bash
   node tools/embed-demo/server.js
   ```

Também roda pelo painel de preview do Claude (`.claude/launch.json`, configuração `embed-demo`).
Precisa de Node 18 ou mais novo e não tem dependências.
