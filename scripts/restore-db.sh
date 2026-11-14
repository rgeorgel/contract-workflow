#!/bin/sh
# Restaura um backup gerado por backup-db.sh. APAGA os dados atuais do banco de destino.
# Uso (com o Compose): docker compose --profile backup run --rm backup /scripts/restore-db.sh /backups/contratos-AAAAMMDD-HHMMSS.dump
set -eu

file="${1:?informe o arquivo .dump}"
[ -f "$file" ] || { echo "arquivo não encontrado: $file" >&2; exit 1; }

echo "Restaurando $file em ${PGHOST:-localhost}/${PGDATABASE:-contratos}; os dados atuais serão substituídos."
pg_restore --clean --if-exists --no-owner --single-transaction --dbname="${PGDATABASE:-contratos}" "$file"
echo "restauração concluída"
