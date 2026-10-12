# Docker self-hosted runner

This runner is intended for the private `blackfalc0ns/mealmeta_backend` repository. It uses the Linux labels required by the CI and MonsterASP workflows and mounts the host Docker socket so PostgreSQL test containers can run.

1. In GitHub open **Settings → Actions → Runners → New self-hosted runner → Linux x64** and copy a fresh registration token. Do not commit or paste the token into a file.
2. From this directory, run:

```bash
export GITHUB_REPOSITORY=blackfalc0ns/mealmeta_backend
export RUNNER_TOKEN='<fresh-token>'
docker compose up -d --build
docker compose logs -f github-runner
```

3. Confirm the runner is **Idle/Online**, then re-run the waiting workflows.

The token is only used for registration and must be rotated if it is exposed. Keep the repository private because workflow code runs on the machine that owns the Docker socket.
