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
- Criar, editar, duplicar, arquivar; corpo em HTML com variáveis `{{chave}}` tipadas (texto, número, data, moeda, CPF/CNPJ)
- Rascunho + versões publicadas imutáveis; concorrência otimista com ETag/If-Match
- Sanitização no servidor (sem scripts nem URLs externas, para o Gotenberg não buscar nada na rede) e pré-visualização com dados de exemplo

**Fase 3 (contratos e signatários)**
- Assistente em 4 passos: template → dados (formulário gerado pelas variáveis, validado por tipo) → signatários (papel, ordem, e-mail/WhatsApp/SMS) → revisar e enviar
- PDF gerado no Gotenberg, congelado no storage S3 com SHA-256; número sequencial por organização
- Envio pela outbox com retentativa (1, 5, 15, 60 min); depois de 5 falhas o contrato mostra "Erro no envio" com "Tentar de novo"
- Webhooks em `/api/v1/webhooks/autentique` (HMAC-SHA256, idempotentes via `webhook_inbox`); expiração automática
- Tela de detalhe com progresso, linha do tempo, downloads, cancelar, reenviar link e exclusão com confirmação pelo número (`deletion_log`)
- Provedor **fake** até a fase 4: na tela do contrato, a faixa "Simular" faz o papel do signatário (abrir, assinar, recusar, falha na entrega)

**Fase 5 (painel e busca)**
- Painel com contadores por status, pendentes há mais de 3 dias e concluídos recentes (`GET /api/v1/dashboard/summary`)
- Busca full-text em português sem acento (`tsvector` com config `pt_unaccent`, prefixo de palavra) em título, número, valores das variáveis e signatários, mais busca parcial por nome/e-mail (trigram)
- Filtros na URL: status, template, criado por, período de criação ou de conclusão

## Rodando

Tudo em Docker (app em http://localhost:7700, e-mails em http://localhost:8026):

```bash
cp .env.example .env
docker compose up -d --build
```

Ou só os serviços de apoio no Docker e a app com `dotnet run` (também em http://localhost:7700; pare o container `app` antes):

```bash
docker compose up -d db mailpit
dotnet run --project src/Contratos.Api
```

As migrations são aplicadas na subida em desenvolvimento. A documentação da API fica em `/scalar` (só em Development).

## Testes

Precisam do Docker rodando (Testcontainers sobe um Postgres real):

```bash
dotnet test
```

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
