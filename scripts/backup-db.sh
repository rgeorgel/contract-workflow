#!/bin/sh
# Backup do Postgres em formato custom (pg_restore), com retenção.
# Uso: backup-db.sh            → um backup e sai
#      backup-db.sh --loop     → um backup a cada BACKUP_INTERVAL_HOURS (serviço "backup" do Compose)
# Variáveis: PGHOST, PGUSER, PGDATABASE, PGPASSWORD, BACKUP_DIR (/backups),
#            BACKUP_RETENTION_DAYS (14), BACKUP_INTERVAL_HOURS (24)
set -eu

BACKUP_DIR="${BACKUP_DIR:-/backups}"
RETENTION="${BACKUP_RETENTION_DAYS:-14}"
INTERVAL_HOURS="${BACKUP_INTERVAL_HOURS:-24}"

run_backup() {
  ts="$(date -u +%Y%m%d-%H%M%S)"
  target="$BACKUP_DIR/${PGDATABASE:-contratos}-$ts.dump"
  mkdir -p "$BACKUP_DIR"
  # Grava em .partial e renomeia: um backup pela metade nunca parece completo.
  pg_dump --format=custom --no-owner --file="$target.partial"
  mv "$target.partial" "$target"
  echo "backup: $target ($(du -h "$target" | cut -f1))"
  find "$BACKUP_DIR" -name '*.dump' -type f -mtime +"$RETENTION" -print -delete | sed 's/^/backup: removido /'
}

if [ "${1:-}" = "--loop" ]; then
  while true; do
    run_backup || echo "backup: FALHOU em $(date -u)" >&2
    sleep $((INTERVAL_HOURS * 3600))
  done
else
  run_backup
fi
