# MealMate Backend

.NET 9 Clean Architecture: API, Application, Domain, Persistence, Worker, and tests.

## Database

PostgreSQL (PostGIS) via EF Core 9. Schema covers Foundation + M01…M12.

See [src/MealMate.Persistence/SCHEMA.md](src/MealMate.Persistence/SCHEMA.md).

```bash
docker compose up -d
dotnet ef database update --project src/MealMate.Persistence --startup-project src/MealMate.Api
```

Migrations:
1. `InitialIdentity`
2. `M00_FoundationThroughM12_DomainSchema` (~126 domain tables)
