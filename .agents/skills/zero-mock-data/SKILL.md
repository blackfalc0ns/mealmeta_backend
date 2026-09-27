---
name: zero-mock-data
description: Use when developing, modifying, or reviewing backend code (APIs, services, repositories, queries, handlers, migrations, or seed data) to strictly enforce zero fake, mocked, or placeholder business data in production code.
---

# SKILL: ZERO MOCK DATA — PRODUCTION BACKEND

## Purpose

This skill enforces a strict **ZERO MOCK DATA** policy across the entire backend codebase.

The backend must behave as a real production system at all times.

Never introduce fake, mocked, placeholder, hardcoded, simulated, fabricated, temporary, or randomly generated business data into production code.

---

# 1. ABSOLUTE RULE

**DO NOT USE MOCK DATA IN THE BACKEND.**

All business data returned by the backend must originate from a real, valid source such as:

- Database
- Repository
- External API
- Cache populated from real data
- Authenticated user/session data
- Configuration / environment variables
- Real file/storage provider
- Real message/event source
- Real domain computation based on persisted data

Never fabricate data simply to make an endpoint, service, UI, integration, test flow, or feature appear functional.

---

# 2. FORBIDDEN PATTERNS

Never introduce production code containing patterns such as:

```csharp
return new[]
{
    new ProductDto { Id = 1, Name = "Test Product" },
    new ProductDto { Id = 2, Name = "Sample Product" }
};
```

Forbidden examples include:

```csharp
var fakeUser = new User
{
    Id = Guid.NewGuid(),
    Name = "John Doe"
};
```

```csharp
return Task.FromResult(new DashboardDto
{
    TotalOrders = 120,
    Revenue = 50000,
    Customers = 300
});
```

```csharp
var random = new Random();
result.Score = random.Next(50, 100);
```

```csharp
return new List<string>
{
    "Sample",
    "Demo",
    "Mock"
};
```

Also forbidden:

- hardcoded IDs
- fake GUIDs
- fake emails
- fake phone numbers
- fake products
- fake users
- fake orders
- fake prices
- fake inventory
- fake reviews
- fake statistics
- fake analytics
- fake dashboard values
- fake notifications
- fake call history
- fake transactions
- fake pharmacy data
- fake AI results
- random values pretending to be real
- placeholder responses
- temporary static collections
- in-memory production repositories
- fallback fabricated objects when data does not exist

---

# 3. NO SILENT FAKE FALLBACKS

Never use fake data as a fallback.

Wrong:

```csharp
var product = await repository.GetByIdAsync(id)
              ?? new Product
              {
                  Name = "Unknown Product",
                  Price = 100
              };
```

Correct:

```csharp
var product = await repository.GetByIdAsync(id);

if (product is null)
{
    throw new NotFoundException("Product not found.");
}
```

The system must expose the real state of the application.

If data does not exist, return the appropriate:

- empty result
- null where contractually valid
- 404
- validation error
- domain error
- dependency failure

Do not invent data.

---

# 4. EMPTY DATABASE MEANS EMPTY RESPONSE

If the database contains zero records, the API must return a legitimate empty result.

Example:

```json
{
  "items": [],
  "totalCount": 0
}
```

Never populate the result with sample records just because the database is empty.

---

# 5. REAL DATABASE ONLY

Production application services, handlers, repositories, controllers, queries, jobs, background workers, SignalR hubs, and domain services must use the actual persistence layer.

Examples:

- EF Core DbContext
- repositories backed by EF Core
- Redis-backed cache
- real storage service
- real external provider

Do not replace missing implementations with an in-memory fake repository.

Wrong:

```csharp
services.AddSingleton<IProductRepository, FakeProductRepository>();
```

unless this registration exists exclusively inside an isolated test project.

---

# 6. NO PLACEHOLDER IMPLEMENTATIONS

Do not complete unfinished functionality by returning placeholders.

Forbidden:

```csharp
public async Task<Result> ExecuteAsync()
{
    // TODO implement later

    return Result.Success();
}
```

Forbidden:

```csharp
return new ResponseDto
{
    Status = "Success"
};
```

when the real operation was not performed.

Instead:

- implement the real logic
- clearly fail
- leave an explicit unsupported state

Never report success for work that did not happen.

---

# 7. EXTERNAL APIs

If a feature depends on an external API, use the real provider integration.

Do not silently replace an unavailable provider with fake output.

Wrong:

```csharp
try
{
    return await provider.GetDataAsync();
}
catch
{
    return FakeData.Create();
}
```

Correct behavior:

```csharp
try
{
    return await provider.GetDataAsync();
}
catch (Exception ex)
{
    logger.LogError(ex, "External provider failed.");
    throw new ExternalServiceException(...);
}
```

Failures must remain visible and observable.

---

# 8. AI FEATURES

AI-related services must never fabricate an AI response if the actual model/provider did not produce one.

Forbidden:

```csharp
return new AiResult
{
    Confidence = 0.95,
    Match = true
};
```

unless those values were actually calculated by the real system.

If the AI provider fails:

- retry according to policy
- return a controlled failure
- mark processing as failed/pending

Do not invent AI output.

---

# 9. DASHBOARDS AND ANALYTICS

Every dashboard number must be calculated from actual persisted data.

Examples:

```csharp
var usersCount =
    await db.Users.CountAsync(cancellationToken);

var ordersCount =
    await db.Orders.CountAsync(cancellationToken);

var revenue =
    await db.Orders
        .Where(x => x.Status == OrderStatus.Completed)
        .SumAsync(x => x.Total, cancellationToken);
```

Never hardcode dashboard statistics.

---

# 10. PAGINATION

Pagination metadata must reflect the real query.

Never write:

```csharp
TotalCount = 100;
```

unless 100 was calculated from the source.

Correct:

```csharp
var totalCount =
    await query.CountAsync(cancellationToken);
```

---

# 11. AUTHENTICATION AND USERS

Never create a fake current user when authentication information is unavailable.

Forbidden:

```csharp
var userId = currentUser.Id ?? Guid.Parse("...");
```

Authentication failures must fail correctly.

Use:

- ClaimsPrincipal
- authentication token
- identity provider
- persisted user account

No default fake user.

---

# 12. CONFIGURATION

Credentials, URLs, IDs, secrets, and environment-specific values must come from:

- appsettings
- environment variables
- secret manager
- strongly typed Options

Do not hardcode environment secrets or infrastructure values.

---

# 13. DATES AND TIMES

Do not hardcode dates pretending to represent current system behavior.

Prefer:

```csharp
TimeProvider
```

or the project's time abstraction.

Example:

```csharp
var now = timeProvider.GetUtcNow();
```

---

# 14. RANDOM DATA

Random values must never be used as real business values.

Random generation is allowed only when it is genuinely part of the domain, such as:

- cryptographic token generation
- secure OTP generation
- identifiers when the application owns their creation

Never use random generation to fabricate:

- prices
- counts
- scores
- reviews
- statuses
- stock
- analytics
- recommendations

---

# 15. GUID GENERATION

`Guid.NewGuid()` is not automatically considered mock data.

It may be used when creating a legitimate new domain entity.

Valid:

```csharp
var order = new Order
{
    Id = Guid.NewGuid(),
    UserId = currentUserId
};
```

Invalid:

```csharp
var fakeUserId = Guid.NewGuid();
```

to bypass missing authentication or relationships.

---

# 16. SEED DATA

Production seed data must be limited to legitimate required system data.

Examples that may be acceptable:

- roles
- permissions
- countries
- system configuration
- enum-backed lookup values

Do not seed fake business entities merely to make the application appear populated.

Examples normally forbidden:

- fake customers
- fake products
- fake reviews
- fake orders
- fake payments
- fake pharmacy inventory

unless the project explicitly defines them as real initial production records.

---

# 17. TESTS ARE THE ONLY GENERAL EXCEPTION

Mocks, stubs, fakes, fixtures, Bogus, AutoFixture, test containers, and generated data are allowed inside automated test projects.

Examples:

```text
*.Tests
*.UnitTests
*.IntegrationTests
*.FunctionalTests
tests/
```

Test data must not leak into production projects.

Never reference test factories from production code.

---

# 18. DEVELOPMENT ENVIRONMENT

Do not automatically assume Development environment means mock data is allowed.

Development should preferably run against:

- local database
- Docker infrastructure
- sandbox provider
- test provider account
- emulator supplied by the real provider

Mock providers may only exist when explicitly requested and must never become the default production implementation.

---

# 19. NO MOCK CONTROLLERS

Never create endpoints such as:

```text
/api/mock/*
/api/demo/*
/api/test-data/*
```

inside the production application unless the project's requirements explicitly demand administrative testing functionality.

---

# 20. NO STATIC BUSINESS COLLECTIONS

Do not keep mutable business state in:

```csharp
static List<T>
Dictionary<TKey,TValue>
ConcurrentDictionary<TKey,TValue>
```

as a substitute for persistent storage.

Use the actual database/cache/storage architecture.

---

# 21. DTO DEFAULT VALUES

Do not assign business-looking default values that can be mistaken for real information.

Bad:

```csharp
public decimal Price { get; set; } = 99.99m;
public int Stock { get; set; } = 10;
```

Defaults should only represent legitimate semantic defaults.

---

# 22. ERROR HANDLING

Never hide a real error by returning fabricated success data.

Forbidden:

```csharp
catch
{
    return DefaultResponse();
}
```

Instead:

- log the real error
- convert it to the project's defined error type
- allow global exception handling to process it
- retry when appropriate

---

# 23. AGENT IMPLEMENTATION RULE

Whenever implementing a requested feature:

1. Inspect the existing data model.
2. Find the real entity/repository/provider.
3. Trace the actual data source.
4. Implement the query/command against that source.
5. Preserve architecture and dependency boundaries.
6. Handle missing data explicitly.
7. Handle dependency failures explicitly.
8. Add tests.
9. Never make the feature "work" by fabricating data.

If required real infrastructure does not yet exist, implement the missing infrastructure properly.

Do **not** replace it with mock data.

---

# 24. WHEN REQUIRED INFORMATION IS MISSING

If implementation requires information not currently available, such as:

- missing entity
- missing database field
- missing repository
- missing external API
- missing configuration
- missing authentication context

Do not guess values.

Determine whether the information can be derived from the existing architecture.

If not, clearly identify the missing dependency in the implementation notes instead of fabricating it.

---

# 25. CODE REVIEW CHECK

Before considering any backend task complete, search changed code for suspicious terms:

```text
mock
fake
dummy
sample
demo
testdata
placeholder
hardcoded
random
faker
bogus
temp
temporary
John Doe
example.com
Lorem
```

Also inspect:

```text
Guid.NewGuid()
Random()
new List<
new []
Task.FromResult
static List
static Dictionary
```

These are not always wrong, but every occurrence introduced by the change must have a legitimate reason.

---

# 26. COMPLETION REQUIREMENT

A feature is NOT considered complete if:

- it depends on mock business data
- it uses temporary static records
- it returns fabricated success results
- it silently falls back to fake objects
- it bypasses the database
- it bypasses authentication
- it bypasses an unavailable integration
- it hardcodes business statistics
- it contains placeholder production logic

The feature is complete only when it works through the actual production architecture.

---

# 27. EXISTING MOCK DATA

Whenever working on an existing module, inspect nearby implementation code for mock or placeholder logic.

If existing mock data directly affects the requested functionality:

- remove it
- replace it with the real data source
- preserve existing contracts where possible
- update tests accordingly

Do not blindly refactor unrelated parts of the system unless necessary.

---

# 28. FINAL RESPONSE REQUIREMENT

At the end of every backend implementation task, explicitly confirm:

```text
Mock Data Check:
- No mock business data introduced.
- No fake fallback responses introduced.
- No hardcoded production business values introduced.
- All runtime data comes from real application sources.
```

If an exception exists, identify the exact file and reason.

Never claim this check passed unless the changed code was actually inspected.

---

# CORE PRINCIPLE

**Missing real data must remain missing.  
Failed integrations must remain failures.  
Empty databases must remain empty.  
The backend must never lie to the frontend.**

Production code represents real system state only.
