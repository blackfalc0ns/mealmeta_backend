#!/usr/bin/env bash
set -Eeuo pipefail
umask 027

# Certbot calls deploy hooks only after a successful certificate renewal.
# The API and TURN share this hostname and certificate on the current VPS.
if [[ "${RENEWED_LINEAGE:-}" != /etc/letsencrypt/live/api.trymealmate.com ]]; then
  exit 0
fi

test "$(id -u)" = 0
test -s "$RENEWED_LINEAGE/fullchain.pem"
test -s "$RENEWED_LINEAGE/privkey.pem"
getent group turnserver >/dev/null

install -d -o root -g turnserver -m 0750 /etc/coturn/tls
install -o root -g turnserver -m 0640 "$RENEWED_LINEAGE/fullchain.pem" /etc/coturn/tls/fullchain.pem
install -o root -g turnserver -m 0640 "$RENEWED_LINEAGE/privkey.pem" /etc/coturn/tls/privkey.pem
systemctl try-restart coturn.service
