#!/usr/bin/env bash
set -Eeuo pipefail

# The Actions runner explicitly refuses to configure as root.  The named
# volume is created by Docker as root, so initialise its ownership here, then
# switch to the unprivileged runner account for registration and execution.
chown -R runner:runner /runner
exec runuser -u runner --preserve-environment -- /bin/bash -c 'export HOME=/home/runner; exec /runner/runner-entrypoint.sh'
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
