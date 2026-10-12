#!/usr/bin/env bash
set -Eeuo pipefail

# The Actions runner explicitly refuses to configure as root.  The named
# volume is created by Docker as root, so initialise its ownership here, then
# switch to the unprivileged runner account for registration and execution.
chown -R runner:runner /runner

# Jobs that use container services (PostgreSQL in this repository) require
# Docker access. Match the mounted host socket's group rather than running the
# Actions runner itself as root.
if [[ -S /var/run/docker.sock ]]; then
  socket_gid="$(stat -c '%g' /var/run/docker.sock)"
  socket_group="$(getent group "${socket_gid}" | cut -d: -f1 || true)"
  if [[ -z "${socket_group}" ]]; then
    socket_group="runner-docker"
    groupadd --gid "${socket_gid}" "${socket_group}"
  fi
  usermod --append --groups "${socket_group}" runner
fi

exec runuser -u runner --preserve-environment -- /bin/bash -c 'export HOME=/home/runner; exec /runner-entrypoint.sh'
: "${GITHUB_REPOSITORY:?Set GITHUB_REPOSITORY, for example blackfalc0ns/mealmeta_backend}"
: "${RUNNER_TOKEN:?Set RUNNER_TOKEN from GitHub Settings > Actions > Runners}"

runner_name="${RUNNER_NAME:-mealmate-docker-runner}"
runner_labels="${RUNNER_LABELS:-self-hosted,linux,x64}"

if [[ ! -f /runner/.runner ]]; then
  ./config.sh \
    --unattended \
    --replace \
    --url "https://github.com/${GITHUB_REPOSITORY}" \
    --token "${RUNNER_TOKEN}" \
    --name "${runner_name}" \
    --labels "${runner_labels}" \
    --work _work
fi

trap './config.sh remove --unattended --token "${RUNNER_TOKEN}" || true' TERM INT
exec ./run.sh
