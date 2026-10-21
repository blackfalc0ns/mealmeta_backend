# Driver support issues API implementation plan

## Scope
Add authenticated driver list/detail/photo attachment endpoints and dispatcher progress updates backed only by persisted complaints, trip stops, audit logs, upload records, notifications, and outbox messages. No default or sample business records are introduced.

## Source mapping
- Driver issue inclusion uses the persisted driver profile association plus the supported `DriverIssueCatalog` categories and dedicated `DriverReassignmentRequested` category. Existing driver report audit rows identify the stop, while reassignment reports have a dedicated complaint category. The complaint model has no explicit source field and `CustomerUserId` is populated for driver reports too, so this implementation does not claim that field discriminates source.
- Complaint statuses map to All/UnderReview/AwaitingReply/Resolved while preserving their raw enum. ContactingCustomer only exists when an explicit progress audit exists.
- Order metadata resolves via `ReassignmentTripStopId`, then stop ID, daily order ID, and subscription day. Missing values remain null.
- Photos use UploadFileRecord ownership plus the file storage existence check; returned evidence URLs use CatalogImageStorage.

## Allowed files
The only files in scope are the driver/dispatcher controllers, driver app DTO/query/command/projection files, dispatcher progress command, bilingual API resource files, focused unit/API tests, and this API/plan documentation. No other files should be changed.

## Acceptance and validation
- Authenticated driver scope comes from the current user and their own persisted profile; foreign issues are hidden as 404.
- Counts, filtering, search, pagination, detail timeline, order fields, and protected evidence projection reflect persisted data only.
- Photo binding is transactional and locked, verifies owner/MIME/storage, deduplicates idempotently, and records audit/outbox data.
- Dispatcher progress is role and restaurant scoped, locked, audited, forward-only, idempotent for a repeated stage, and notifies through existing records/outbox.
- Focused unit/API tests cover scope, filtering/projection, photo guards, progress transitions and HTTP routing/authentication.
- Run focused tests and `dotnet build src/MealMate.Api/MealMate.Api.csproj --no-restore`; report unrelated build blockers if present. Deployment is outside scope.

## Verification record
- In the clean publication worktree, `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter FullyQualifiedName~DriverSupportIssuesTests` passed 11/11 tests.
- The broader regression command `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~DriverSupportIssuesTests|FullyQualifiedName~DriverIssueReassignmentContractTests|FullyQualifiedName~DispatcherDashboardTests"` passed 101/101 tests.
- In the clean publication worktree, `dotnet build src/MealMate.Api/MealMate.Api.csproj` passed with 0 warnings and 0 errors. The first test run caught an invalid explicit base-entity ID assignment on added evidence photos; the code now preserves the generated ID, explicitly adds the photo to the EF set, and the test asserts that persisted identity.
- The HTTP test project compiled, but the two `DriverSupportIssuesHttpTests` could not initialize `OrderLifecycleEndToEndFixture`: Testcontainers reported that Docker was not running or misconfigured (`DockerEndpointAuthConfig`). The HTTP assertions therefore did not run.
- No deployment was performed.
