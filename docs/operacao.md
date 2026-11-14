# Operação

## Qual arquivo do Compose é lido

| Arquivo | Conteúdo | Quando entra |
| --- | --- | --- |
| `docker-compose.yml` | app, Gotenberg, S3 (SeaweedFS), backup | Sempre |
| `docker-compose.override.yml` | Postgres local, Mailpit, portas de debug, ajustes de dev | **Automaticamente**, se o arquivo existir na pasta |

O `docker compose` procura o `docker-compose.override.yml` sozinho e junta com o base. Para usar **só o base**, escolha um jeito:

1. **`COMPOSE_FILE` no `.env`** (recomendado no servidor): com `COMPOSE_FILE=docker-compose.yml`, todo `docker compose ...` naquela pasta ignora o override, sem precisar lembrar de flag. O [.env.example](../.env.example), modelo do servidor, já vem assim; o de desenvolvimento é o [.env.example.local](../.env.example.local).
2. **`-f` explícito**: `docker compose -f docker-compose.yml up -d`. Repita o `-f` em todo comando (`logs`, `ps`, `down`).
3. **Não copiar o override** para o servidor.

Para conferir o que vai rodar: `docker compose config --services` lista os serviços, e `docker compose config` mostra a configuração final já mesclada.

## Servidor de testes

```bash
cp .env.example .env   # preencha DB_HOST, DB_PASSWORD, S3_*, RESEND_API_KEY, EMAIL_FROM, PUBLIC_BASE_URL
docker compose up -d --build
curl -fs http://localhost:7700/health/ready
```

- O banco é externo: crie o database e o usuário antes. Com `MIGRATE_ON_STARTUP=true`, a app cria as tabelas na subida; o usuário precisa poder criar extensões (`unaccent`, `pg_trgm`) ou elas devem ser criadas antes por um superusuário.
- Fora de `Development` os cookies são `Secure`: o acesso precisa ser por HTTPS (proxy na frente, com `FORWARDED_HEADERS=true`). Por HTTP o login não funciona.
- Sem `DB_HOST` ou `DB_PASSWORD` no `.env`, o Compose para com um erro dizendo o que falta.

## E-mails (Resend)

Confirmação de cadastro, convites e nova senha saem pela [Resend](https://resend.com) (`EMAIL_PROVIDER=Resend`, o padrão do `docker-compose.yml`):

1. Em resend.com/domains, adicione o domínio do remetente e publique os registros DNS (SPF/DKIM) que ela mostrar, até o domínio aparecer como verificado.
2. Em resend.com/api-keys, crie uma chave com permissão **Sending access** (de preferência restrita a esse domínio).
3. No `.env` do servidor: `RESEND_API_KEY=re_...`, `EMAIL_FROM=nao-responda@seu-dominio` (do domínio verificado) e, se quiser, `EMAIL_FROM_NAME`.

Sem `RESEND_API_KEY` a app não sobe e o log diz o que falta. Se a Resend devolver 429 ou 5xx, a app tenta de novo até 3 vezes com a mesma chave de idempotência (o e-mail não sai duplicado); outros erros (remetente não verificado, chave inválida) aparecem no log com o código e o nome do erro da Resend, sem o destinatário. Para usar um SMTP qualquer no lugar: `EMAIL_PROVIDER=Smtp` e as variáveis `SMTP_*`.

Em desenvolvimento o `docker-compose.override.yml` força `Email__Provider=Smtp` com o Mailpit (http://localhost:8026): nada sai para a Resend.

## Saúde

| Rota | Para quê | Olha |
| --- | --- | --- |
| `GET /health/live` | Reiniciar o processo travado (liveness) | Só se a app responde |
| `GET /health/ready` | Tirar do balanceador quando algo essencial cai (readiness) | Banco e storage (falha = 503); Gotenberg (falha = degradado, 200) |

As respostas trazem só o status por dependência, sem mensagens internas.

## Produção atrás de proxy

A app escuta HTTP na porta 7700. Em produção, ponha um proxy com TLS na frente (Caddy, Nginx, load balancer) e:

- `FORWARDED_HEADERS=true` para a app ver o IP real (rate limit) e o esquema https (cookies `Secure`, HSTS);
- não exponha a porta da app diretamente: com `FORWARDED_HEADERS=true` ela confia no `X-Forwarded-For` de quem estiver na frente;
- `PUBLIC_BASE_URL=https://seu-dominio` (links dos e-mails);
- `ASPNETCORE_ENVIRONMENT=Production` (HSTS, logs em JSON, cookies sempre `Secure`, sem Scalar);
- migrations com bundle antes do deploy: `dotnet ef migrations bundle -p src/Contratos.Infrastructure -s src/Contratos.Api` e rode o executável com a connection string de produção.

## Limites

| Variável | Padrão | Escopo |
| --- | --- | --- |
| `RateLimiting__AuthPermitsPerMinute` | 10 | Cadastro, login, confirmação, senha: por IP |
| `API_RATE_LIMIT_PER_MINUTE` | 300 | Toda a `/api`: por usuário logado, ou por IP |

## Backup

### Banco

```bash
docker compose --profile backup up -d backup
```

Faz `pg_dump` a cada 24 h em `./backups` (`BACKUP_DIR`) e apaga os com mais de 14 dias (`BACKUP_RETENTION_DAYS`). Um backup avulso:

```bash
docker compose --profile backup run --rm --entrypoint /bin/sh backup /scripts/backup-db.sh
```

Restaurar (substitui os dados do banco de destino):

```bash
docker compose --profile backup run --rm --entrypoint /bin/sh backup /scripts/restore-db.sh /backups/contratos-AAAAMMDD-HHMMSS.dump
```

Teste a restauração de vez em quando num banco separado (`-e PGDATABASE=contratos_teste`). Leve a pasta de backups para fora da máquina (outro bucket, outra região).

### Arquivos

O banco aponta para os PDFs; um sem o outro não basta. No S3 de produção:

- ative **versionamento** no bucket (exclusão acidental vira versão anterior);
- ative **replicação** para outra região ou conta;
- regra de ciclo de vida para expirar versões antigas depois de N dias, se desejado.

## Logs

Em produção, JSON compacto no stdout (`docker compose logs app`). Cada requisição traz método, caminho, status, duração, `UserId` e `OrganizationId`. Ver [lgpd.md](lgpd.md) sobre o que não vai para o log.
