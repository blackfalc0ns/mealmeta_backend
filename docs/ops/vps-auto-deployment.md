# VPS automatic deployment

The Ubuntu VPS runs the API directly with .NET 9 and systemd, behind Nginx.
There is no Docker dependency and no GitHub Actions deployment job.

Push to dev. The mealmate-deploy.timer checks that branch approximately every
minute after the previous check completes. A changed commit is fetched and
published on the server. Only the API and its project dependencies are built;
automated tests do not run.

The repository is kept at /opt/mealmate/repository.git. Builds use separate
directories under /opt/mealmate/releases. Runtime uploads and logs remain in
/opt/mealmate/shared. Secrets are in /etc/mealmate/runtime.env with root-only
permissions and are never written into the repository.

Before activating a release, deployment backs up the existing mealmate_db
database under /opt/mealmate/backups. It then switches the current symlink,
restarts mealmate-api.service and checks http://127.0.0.1:5000/health/ready.
An unchanged commit skips building. A failed build leaves the current version
alone. A failed readiness check restores the previous application release
when one exists. Schema rollback is not automatic; retain and review database
backups before recovery. Releases and backups are retained for manual review.

Commands on the VPS:

- systemctl status mealmate-api
- systemctl list-timers mealmate-deploy.timer
- journalctl -u mealmate-deploy -n 100 --no-pager
- journalctl -u mealmate-api -n 100 --no-pager
- systemctl start mealmate-deploy.service
- systemctl stop mealmate-deploy.timer
- cat /opt/mealmate/deployed-commit

The initial public origin is http://157.173.104.121. A domain and HTTPS remain
necessary before moving production login/payment clients to this origin.
Payment callbacks require the configured HTTPS public origin, and existing
encrypted uploads must be migrated or served from their existing source.
The old MonsterASP deployment workflow has been removed. Its PowerShell
script remains available for manual recovery only.

The initial server validation disables startup migrations and subscription /
voice background workers to avoid concurrent work with the old host.
Before final cutover, migrate the required schema with a fresh backup, verify
the new API, stop the old host workers, then enable the corresponding workers
in runtime.env and restart the API. Preserve real production signing and
encryption keys when clients and encrypted data are migrated.
