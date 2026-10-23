#!/usr/bin/env bash
set -Eeuo pipefail
umask 027

base=/opt/mealmate
repo="$base/repository.git"
exec 9>/run/lock/mealmate-deploy.lock
flock -n 9 || exit 0
test -s /etc/mealmate/runtime.env || { echo "Missing runtime configuration"; exit 1; }

as_deployer() { runuser -u mealmate-deploy -- env HOME=/home/mealmate-deploy "$@"; }
as_deployer git --git-dir="$repo" fetch --quiet origin refs/heads/dev:refs/heads/dev
commit="$(as_deployer git --git-dir="$repo" rev-parse refs/heads/dev)"
if [[ -f "$base/deployed-commit" ]] && [[ "$(cat "$base/deployed-commit")" == "$commit" ]]; then
  echo "Already deployed $commit"
  exit 0
fi

# Every build uses a fresh directory, so a failed build is never promoted.
release="$(mktemp -d "$base/releases/$commit.XXXXXX")"
chown mealmate-deploy:mealmate "$release"
as_deployer mkdir "$release/source" "$release/app" "$release/worker"
as_deployer git --git-dir="$repo" archive "$commit" | as_deployer tar -x -C "$release/source"
as_deployer dotnet publish "$release/source/src/MealMate.Api/MealMate.Api.csproj" \
  --configuration Release --output "$release/app" --nologo \
  -p:DebugType=None -p:DebugSymbols=false
test -s "$release/app/MealMate.Api.dll"
as_deployer dotnet publish "$release/source/src/MealMate.Worker/MealMate.Worker.csproj" \
  --configuration Release --output "$release/worker" --nologo \
  -p:DebugType=None -p:DebugSymbols=false
test -s "$release/worker/MealMate.Worker.dll"
chmod -R g+rX "$release"

# Keep runtime data outside release directories.
for directory in uploads logs; do
  if [[ -e "$release/app/$directory" ]]; then
    mv "$release/app/$directory" "$release/app/$directory.publish"
  fi
  ln -s "$base/shared/$directory" "$release/app/$directory"
done

previous="$(readlink "$base/current" || true)"
previous_worker="$(readlink "$base/worker-current" || true)"
worker_enabled=0
if systemctl cat mealmate-support-worker.service >/dev/null 2>&1; then worker_enabled=1; fi
# Preserve the database before startup can apply pending EF migrations.
backup="$base/backups/pre-$commit-$(date -u +%Y%m%dT%H%M%SZ).dump"
runuser -u postgres -- pg_dump --format=custom mealmate_db > "$backup"
test -s "$backup"
chmod 600 "$backup"
if [[ "$worker_enabled" == 1 ]]; then systemctl stop mealmate-support-worker.service || true; fi

ln -s "$release/app" "$base/current.next"
mv -Tf "$base/current.next" "$base/current"
ln -s "$release/worker" "$base/worker-current.next"
mv -Tf "$base/worker-current.next" "$base/worker-current"
systemctl restart mealmate-api.service
ready=0
for attempt in $(seq 1 60); do
  if systemctl is-active --quiet mealmate-api.service &&
     curl --fail --silent --max-time 3 http://127.0.0.1:5000/health/ready >/dev/null; then
    ready=1
    break
  fi
  sleep 2
done
if [[ "$ready" != 1 ]]; then
  echo "Readiness failed for $commit; database backup: $backup"
  systemctl stop mealmate-api.service
  if [[ "$worker_enabled" == 1 ]]; then systemctl stop mealmate-support-worker.service || true; fi
  if [[ -n "$previous" ]]; then
    ln -s "$previous" "$base/current.rollback"
    mv -Tf "$base/current.rollback" "$base/current"
    systemctl start mealmate-api.service
    if [[ "$worker_enabled" == 1 && -n "$previous_worker" ]]; then
      ln -s "$previous_worker" "$base/worker-current.rollback"
      mv -Tf "$base/worker-current.rollback" "$base/worker-current"
      systemctl start mealmate-support-worker.service
    fi
    echo "Previous application release restored; database schema was not rolled back."
  fi
  exit 1
fi
if [[ "$worker_enabled" == 1 ]]; then
  systemctl restart mealmate-support-worker.service
  sleep 2
  systemctl is-active --quiet mealmate-support-worker.service || { echo "Support worker failed after API readiness; inspect service logs."; exit 1; }
fi
printf '%s\n' "$commit" > "$base/deployed-commit"
echo "Deployed $commit; readiness passed."
