# Windows API deployment

Pushes to dev and manual workflow dispatch run one job on GitHub-hosted
windows-latest. It restores and publishes only src/MealMate.Api and its project
dependencies, then synchronizes the publish directory to MonsterASP using
WinSCP SFTP. Automated tests, coverage, EF model checks, Docker services and
self-hosted runners are no longer part of this deployment.

The CI test workflow was removed. Test source projects remain available for
local use. The existing independent CodeQL workflow is unchanged.

Use existing GitHub secrets FTP_SERVER, FTP_USERNAME, FTP_PASSWORD and
FTP_SERVER_DIR. The previous SERVER_COMPUTER_NAME, SERVER_USERNAME,
SERVER_PASSWORD and WEBSITE_NAME fallbacks remain supported. The directory
defaults to /wwwroot/. FTP_HOST_KEY optionally pins the SFTP host fingerprint;
otherwise WinSCP accepts the host key on the first connection.

Only changed files are uploaded. Remote-only files are preserved, and PDBs,
logs, old assemblies and app_offline.htm are excluded from synchronization.
IIS is stopped with app_offline.htm before replacing assemblies. Successful
upload must be followed by successful removal of that file. On upload failure
the file stays in place; rerun the deployment after correcting the reported
error rather than serving a partial upload.

The SFTP step has a ten-minute timeout; the whole job has a twenty-minute
timeout. These are failure limits, not a guarantee that publishing succeeds
within ten minutes. Actual duration depends on checkout, NuGet and SFTP
throughput. GitHub-hosted runner billing/payment restrictions must be cleared
in GitHub Billing and plans before this workflow can start.

WinSCP integration follows the official
[installation](https://winscp.net/eng/docs/library_install) and
[synchronization](https://winscp.net/eng/docs/library_session_synchronizedirectories)
documentation.
