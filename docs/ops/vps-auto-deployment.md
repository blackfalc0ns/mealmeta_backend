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

The public API origin is https://api.trymealmate.com. Nginx terminates HTTPS
and forwards WebSocket upgrades to the API. Payment callbacks require the
configured HTTPS public origin, and existing
encrypted uploads must be migrated or served from their existing source.
The old MonsterASP deployment workflow has been removed. Its PowerShell
script remains available for manual recovery only.

Startup migrations are enabled after the initial readiness check, with a
fresh database backup before each release activation. The voice recovery
worker is enabled on this host as of 2026-10-16. Subscription workers retain
their separate cutover setting; enabling voice recovery does not enable them.
Voice recovery uses PostgreSQL advisory coordination and conditional lifecycle
transitions. Before migrating another worker, verify its ownership and old-host
state separately. Preserve real production signing and encryption keys when clients
and encrypted data are migrated. The VPS initially has its own generated
signing key, so sessions issued by the old API are not accepted on this origin.

## Voice-call runtime configuration

The current mobile release targets Android. Voice-calls v2, recovery, FCM,
and authenticated TURN are enabled. iOS needs its real APNs/PushKit credentials
and application bundle identifiers before it can be supported.

Firebase uses `/etc/mealmate-secrets/firebase-service-account.json`, owned by
`root:mealmate` with mode `0640` inside a directory with mode `0750`.
`Firebase__CredentialPath` and `GOOGLE_APPLICATION_CREDENTIALS` point to that
absolute path in `/etc/mealmate/runtime.env`; `Firebase__ProjectId` matches the
service account's project. Keep the private key outside the repository and
release directories. Do not replace it with a development example on deploy.

Coturn uses `api.trymealmate.com`, port 3478 for UDP/TCP, port 5349 for TLS,
and UDP relay ports 49152–50151 on this host. Keep the API's Coturn shared
secret identical to the private Coturn configuration. `Coturn__EnableTls` and
`Coturn__TlsCertConfigured` are true after verifying the listener and certificate.
The TLS listener uses the hostname certificate, not a certificate for the IP.

`ops/vps/coturn-certificate-renewal.sh` is installed as
`/etc/letsencrypt/renewal-hooks/deploy/mealmate-coturn` with mode `0750`.
It copies successfully renewed API certificates into `/etc/coturn/tls` with
`root:turnserver` ownership and mode `0640`, then restarts active Coturn.
Do not grant the service access to all of `/etc/letsencrypt`.
To install the hook from a checkout on the VPS:

```bash
install -m 0750 ops/vps/coturn-certificate-renewal.sh /etc/letsencrypt/renewal-hooks/deploy/mealmate-coturn
bash -n /etc/letsencrypt/renewal-hooks/deploy/mealmate-coturn
RENEWED_LINEAGE=/etc/letsencrypt/live/api.trymealmate.com /etc/letsencrypt/renewal-hooks/deploy/mealmate-coturn
```

Voice recovery requires `BackgroundJobs__VoiceCallsEnabled=true` and a service
restart after a configuration change. Confirm `VoiceCallRecoveryJob started`
and the absence of recovery-cycle errors in the service journal. An API readiness
response alone does not validate FCM credentials, device delivery, or TURN relay.

After changing voice configuration, check HTTPS readiness, service-readable
Firebase credentials, FCM validation-only requests for the customer and driver,
and external authenticated TURN allocations over UDP, TCP, and verified TLS.
Then test an actual Android call in both foreground and background, including
ringing ACK, audio in both directions, hangup, expiry, and busy-state release.
Provider acceptance and a successful allocation do not establish audible media.
