#!/usr/bin/env bash
# Stack própria para os testes e2e: outro projeto do Compose (contratos-e2e), outras portas, banco e
# arquivos separados, provedor de assinatura fake e sem Stripe. Não mexe na stack de desenvolvimento
# nem depende do .env: tudo o que importa vem daqui (variáveis do shell valem mais que as do .env).
#
#   ./stack.sh up     sobe (ou atualiza) e espera a app responder
#   ./stack.sh down   derruba e apaga os dados
#   ./stack.sh test   sobe e roda a suíte inteira contra ela
set -euo pipefail
cd "$(dirname "$0")/.."

export COMPOSE_PROJECT_NAME=contratos-e2e
export APP_PORT=7710 DB_PUBLISHED_PORT=5444 S3_PORT=8343 GOTENBERG_PORT=3015 MAILPIT_UI_PORT=8036 MAILPIT_SMTP_PORT=1036
export PUBLIC_BASE_URL=http://localhost:7710
export SIGNATURE_PROVIDER=Fake AUTENTIQUE_API_KEY= AUTENTIQUE_WEBHOOK_SECRET= AUTENTIQUE_ALERT_EMAIL=
export STRIPE_SECRET_KEY= STRIPE_WEBHOOK_SECRET=
export BILLING_DEFAULT_PLAN=avulso BILLING_CONTACT_EMAIL=
export PLATFORM_API_KEY=e2e-platform-key-0123456789abcdef0123456789
export DB_HOST=db DB_PORT=5432 DB_SSL_MODE=disable DB_NAME=contratos DB_USER=contratos DB_PASSWORD=e2e-senha-do-banco
export S3_SERVICE_URL=http://s3:8333 S3_BUCKET=contratos S3_ACCESS_KEY=e2e S3_SECRET_KEY=e2e-segredo-do-s3 S3_CREATE_BUCKET=true

base_url="http://localhost:${APP_PORT}"

up() {
  docker compose up -d --build app
  for _ in $(seq 1 60); do
    if curl -fs "${base_url}/health/live" > /dev/null; then echo "Stack e2e em ${base_url} (Mailpit: http://localhost:${MAILPIT_UI_PORT})"; return 0; fi
    sleep 2
  done
  echo "A app não respondeu em ${base_url}" >&2
  docker compose logs --tail 50 app >&2
  return 1
}

case "${1:-}" in
  up) up ;;
  down) docker compose down -v ;;
  test)
    up
    cd e2e
    BASE_URL="${base_url}" MAILPIT_URL="http://localhost:${MAILPIT_UI_PORT}" PLATFORM_API_KEY="${PLATFORM_API_KEY}" npx playwright test "${@:2}"
    ;;
  *) echo "uso: $0 up|down|test [argumentos do playwright]" >&2; exit 2 ;;
esac
