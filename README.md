# contract-workflow

SaaS de workflow de contratos: templates com variáveis, geração de contratos e assinatura pela Autentique (e-mail, WhatsApp ou SMS).

## Estado atual

**Fase 1 (fundação)**
- Landing page (`/`), cadastro de organização com confirmação de e-mail, login e recuperação de senha
- Convite de usuários por e-mail (Admin), aceite do convite, perfis Admin, Editor e Leitor, desativação de acesso
- Configurações da organização (nome, lembrete, mensagem padrão, idioma) e uso do mês
- Isolamento entre organizações, antiforgery em toda escrita, rate limiting nas rotas de autenticação, CSP restritiva
- CI no GitHub Actions (build, testes com Postgres real e imagem Docker)

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
