#!/usr/bin/env bash
set -Eeuo pipefail
umask 027
test "$(id -u)" = 0
command -v dotnet >/dev/null
command -v nginx >/dev/null
source_directory="$(cd "$(dirname "$0")" && pwd)"
getent group mealmate >/dev/null || groupadd --system mealmate
id mealmate >/dev/null 2>&1 || useradd --system --gid mealmate --home-dir /opt/mealmate/shared --shell /usr/sbin/nologin mealmate
id mealmate-deploy >/dev/null 2>&1 || useradd --create-home --gid mealmate --shell /usr/sbin/nologin mealmate-deploy
install -d -m 0750 /opt/mealmate /opt/mealmate/backups /etc/mealmate
install -d -o mealmate-deploy -g mealmate -m 0750 /opt/mealmate/releases
install -d -o mealmate -g mealmate -m 0750 /opt/mealmate/shared /opt/mealmate/shared/uploads /opt/mealmate/shared/logs
chgrp mealmate /opt/mealmate
if [[ ! -d /opt/mealmate/repository.git ]]; then
  install -d -o mealmate-deploy -g mealmate -m 0750 /opt/mealmate/repository.git
  runuser -u mealmate-deploy -- git init --bare /opt/mealmate/repository.git
  runuser -u mealmate-deploy -- git --git-dir=/opt/mealmate/repository.git remote add origin https://github.com/blackfalc0ns/mealmeta_backend.git
fi
install -m 0750 "$source_directory/deploy.sh" /usr/local/sbin/mealmate-deploy
install -m 0644 "$source_directory/mealmate-api.service" /etc/systemd/system/mealmate-api.service
install -m 0644 "$source_directory/mealmate-deploy.service" /etc/systemd/system/mealmate-deploy.service
install -m 0644 "$source_directory/mealmate-deploy.timer" /etc/systemd/system/mealmate-deploy.timer
install -m 0644 "$source_directory/nginx.conf" /etc/nginx/sites-available/mealmate-api
# Preserve the stock welcome-site config; disable its link instead of deleting it.
if [[ -L /etc/nginx/sites-enabled/default ]]; then
  mv /etc/nginx/sites-enabled/default /etc/nginx/default-site.disabled
fi
ln -sfn /etc/nginx/sites-available/mealmate-api /etc/nginx/sites-enabled/mealmate-api
nginx -t
systemctl daemon-reload
systemctl enable nginx mealmate-api.service
systemctl reload nginx
echo "Installed. Configure /etc/mealmate/runtime.env before enabling the deploy timer."
