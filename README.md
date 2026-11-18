# contract-workflow

SaaS de workflow de contratos: templates com variáveis, geração de contratos e assinatura pela Autentique (e-mail, WhatsApp ou SMS).

## Estado atual

**Fase 1 (fundação)**
- Landing page (`/`), cadastro de organização com confirmação de e-mail, login e recuperação de senha
- Convite de usuários por e-mail (Admin), aceite do convite, perfis Admin, Editor e Leitor, desativação de acesso
- Configurações da organização (nome, lembrete, mensagem padrão, idioma) e uso do mês
- Isolamento entre organizações, antiforgery em toda escrita, rate limiting nas rotas de autenticação, CSP restritiva
- CI no GitHub Actions (build, testes com Postgres real e imagem Docker)

**Fase 2 (templates)**
- Criar, editar, duplicar, arquivar; corpo em HTML com variáveis `{{chave}}` tipadas (texto, número, data, moeda, CPF/CNPJ) e trechos condicionais `{{#se chave}} … {{/se}}`, que só aparecem com a variável preenchida
- Rascunho + versões publicadas imutáveis; concorrência otimista com ETag/If-Match
- Sanitização no servidor (sem scripts nem URLs externas, para o Gotenberg não buscar nada na rede) e pré-visualização com dados de exemplo

**Fase 3 (contratos e signatários)**
- Assistente em 4 passos: template → dados (formulário gerado pelas variáveis, validado por tipo) → signatários (papel, ordem, e-mail/WhatsApp/SMS) → revisar e enviar
- PDF gerado no Gotenberg, congelado no storage S3 com SHA-256; número sequencial por organização
- Envio pela outbox com retentativa (1, 5, 15, 60 min); depois de 5 falhas o contrato mostra "Erro no envio" com "Tentar de novo"
- Webhooks em `/api/v1/webhooks/autentique` (HMAC-SHA256, idempotentes via `webhook_inbox`); expiração automática
- Tela de detalhe com progresso, linha do tempo, downloads, cancelar, reenviar link e exclusão com confirmação pelo número (`deletion_log`)
- Provedor **fake** para desenvolvimento: na tela do contrato, a faixa "Simular" faz o papel do signatário (abrir, assinar, recusar, falha na entrega)

**Fase 5 (painel e busca)**
- Painel com contadores por status, pendentes há mais de 3 dias e concluídos recentes (`GET /api/v1/dashboard/summary`)
- Busca full-text em português sem acento (`tsvector` com config `pt_unaccent`, prefixo de palavra) em título, número, valores das variáveis e signatários, mais busca parcial por nome/e-mail (trigram)
- Filtros na URL: status, template, criado por, período de criação ou de conclusão

**Fase 6 (endurecimento)**
- `/health/live` e `/health/ready` (banco, storage, Gotenberg), sem detalhes internos na resposta
- Rate limit em toda a API (por usuário ou IP) além das rotas de autenticação; suporte a proxy reverso (`X-Forwarded-*`), HSTS, COOP/CORP, `no-store` nas respostas da API
- Logs sem dados pessoais (só ids de usuário e organização); revisão LGPD em [docs/lgpd.md](docs/lgpd.md)
- Backup diário do banco com retenção e script de restauração (serviço `backup` do Compose)
- Testes de ponta a ponta com Playwright (cadastro, template, contrato até o PDF assinado, busca, celular), também no CI

**Fase 4 (Autentique)**
- `SIGNATURE_PROVIDER=Autentique`: envio pela API GraphQL (upload multipart do PDF, e-mail/WhatsApp/SMS, CPF, ordem, prazo, lembrete), sandbox por configuração
- Webhooks reais (`signature.viewed/accepted/rejected/delivery_failed`, `document.finished/deleted`), com HMAC sobre o corpo bruto
- PDF assinado baixado e arquivado; cancelar vence o prazo e exclui o documento lá
- Reconciliação a cada 30 min para contratos em aberto há mais de 24 h (webhooks perdidos)
- Configuração em [docs/operacao.md](docs/operacao.md#assinatura-autentique)

**Integrações (todos os planos)**
- API pública em `/api/public/v1` com chave por sistema (`Authorization: Bearer ctr_...`): templates, criar e enviar contrato numa chamada (com `Idempotency-Key` e `externalReference`), consultar, cancelar, reenviar link, linha do tempo e PDFs
- Webhooks assinados com HMAC para os eventos dos contratos, com novas tentativas por ~2 dias, histórico de entregas, "Entregar de novo" e proteção contra SSRF
- Embed de templates: o sistema do cliente mostra a tela de templates (editor, variáveis, pré-visualização, publicação) num iframe, com sessão criada pela API, permissões por sessão e só nos sites cadastrados
- Tela de Integrações (Admin) e portal público de documentação em `/desenvolvedores/`, com OpenAPI em `/desenvolvedores/openapi.json`
- Detalhes em [docs/operacao.md](docs/operacao.md#api-pública-e-webhooks)

**Portal super admin**
- Em `/superadmin/`, separado do app (contas, login e cookie próprios; contas criadas pela linha de comando): painel com cadastros, MRR e contratos; lista de organizações com plano, uso, atividade e último acesso; detalhe com linha do tempo
- Trocar plano, conceder créditos, suspender e reativar a conta, com registro de quem fez
- "Entrar como" um usuário do cliente (abre o app em outra aba, 1 hora, acesso total), com cada alteração registrada na auditoria
- Detalhes em [docs/operacao.md](docs/operacao.md#portal-super-admin)

## Rodando

Tudo em Docker (app em http://localhost:7700, e-mails em http://localhost:8026):

```bash
cp .env.example.local .env
docker compose up -d --build
```

O `docker-compose.override.yml` (Postgres local, Mailpit, portas de debug) entra automaticamente em desenvolvimento. No servidor, onde o banco é externo, use só o `docker-compose.yml`: veja [docs/operacao.md](docs/operacao.md#qual-arquivo-do-compose-é-lido) e o modelo [.env.example](.env.example) (o de desenvolvimento é o [.env.example.local](.env.example.local)).

Ou só os serviços de apoio no Docker e a app com `dotnet run` (também em http://localhost:7700; pare o container `app` antes):

```bash
docker compose up -d db mailpit
dotnet run --project src/Contratos.Api
```

As migrations são aplicadas na subida em desenvolvimento. A documentação da API fica em `/scalar` (só em Development).

## Documentação

- [docs/operacao.md](docs/operacao.md): health checks, proxy, limites, API pública e webhooks, backup e restauração
- `/desenvolvedores/`: documentação da API pública para os clientes (portal no próprio site)
- [docs/lgpd.md](docs/lgpd.md): dados pessoais, retenção e medidas técnicas

## Testes

Unitários e de integração (precisam do Docker: Testcontainers sobe um Postgres real):

```bash
dotnet test
```

De ponta a ponta, numa stack própria e isolada (projeto `contratos-e2e`, porta 7710, provedor fake, sem Stripe). Ela não mexe na stack de desenvolvimento nem depende do `.env`:

```bash
cd e2e && npm ci && npx playwright install chromium && npm run test:isolado
```

`npm run stack:down` derruba a stack e apaga os dados dela. Também dá para rodar contra a stack de desenvolvimento (`npx playwright test`, porta 7700). Nesse caso, os testes que precisam simular a assinatura são pulados quando o provedor não é o fake, com o motivo na saída.

## Portas locais

Escolhidas para não colidir com serviços comuns; todas ajustáveis no `.env`.

| Serviço | Porta |
| --- | --- |
| App (Docker ou `dotnet run`) | 7700 |
| Postgres | 5434 |
| Mailpit (caixa de entrada) | 8026 |
| Mailpit (SMTP) | 1026 |
| S3 local (SeaweedFS) | 8333 |
| Gotenberg | 3005 |

## Estrutura

```text
src/
  Contratos.Domain/          entidades e regras (Organization, UserRole, Slug)
  Contratos.Application/     casos de uso, contratos de serviço, e-mails
  Contratos.Infrastructure/  EF Core, Identity, SMTP, migrations
  Contratos.Api/             endpoints, segurança, wwwroot (HTML + CSS + JS puro)
tests/
  Contratos.Tests/           unitários + integração (Testcontainers)
```

Nova migration:

```bash
dotnet ef migrations add <Nome> -p src/Contratos.Infrastructure -s src/Contratos.Api -o Persistence/Migrations
```
