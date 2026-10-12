#!/usr/bin/env bash
set -Eeuo pipefail

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
