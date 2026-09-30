using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.AdminDashboard;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var configBuilder = new ConfigurationBuilder();
var appsettingsPath = Path.Combine(AppContext.BaseDirectory, "../../../src/MealMate.Api/appsettings.Development.json");
if (File.Exists(appsettingsPath))
{
    configBuilder.AddJsonFile(Path.GetFullPath(appsettingsPath), optional: true);
}
configBuilder.AddEnvironmentVariables();
var config = configBuilder.Build();

var connectionString = Environment.GetEnvironmentVariable("MEALMATE_DB_CONNECTION")
    ?? config.GetConnectionString("MealMateDb")
    ?? "Host=157.173.104.121;Port=5432;Database=mealmate_db;Username=mealmate_user;Password=Fahad1990@MealMate;Trust Server Certificate=true;Timeout=60;Command Timeout=120";

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseNpgsql(connectionString, npgsql =>
    {
        npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null);
        npgsql.UseNetTopologySuite();
    })
    .Options;

await using var db = new AppDbContext(options, new NoOpDomainEventDispatcher());

Console.WriteLine("================================================================================");
Console.WriteLine(" MealMate Database State Verifier (PostgreSQL Live Connection)");
Console.WriteLine("================================================================================");

var guids = args
    .Where(a => Guid.TryParse(a, out _))
    .Select(Guid.Parse)
    .ToList();

if (guids.Count == 0)
{
    Console.WriteLine("\n[INFO] No Registration ID supplied. Fetching recent 5 registrations from database...\n");
    var recents = await db.RestaurantRegistrations.AsNoTracking()
        .OrderByDescending(r => r.CreatedAtUtc)
        .Take(5)
        .Select(r => new
        {
            r.Id,
            r.TradeNameAr,
            r.LegalNameAr,
            r.Status,
            r.SubmittedByUserId,
            r.ResultingRestaurantId,
            r.CreatedAtUtc,
            r.ReviewedAtUtc
        })
        .ToListAsync();

    if (recents.Count == 0)
    {
        Console.WriteLine("No registrations found in database.");
        return;
    }

    foreach (var r in recents)
    {
        Console.WriteLine($"• ID: {r.Id}");
        Console.WriteLine($"  Name: {r.TradeNameAr ?? r.LegalNameAr}");
        Console.WriteLine($"  Status: {r.Status}");
        Console.WriteLine($"  Owner UserId: {r.SubmittedByUserId}");
        Console.WriteLine($"  Restaurant Profile: {r.ResultingRestaurantId}");
        Console.WriteLine($"  Created: {r.CreatedAtUtc:yyyy-MM-dd HH:mm:ss} UTC, Reviewed: {r.ReviewedAtUtc?.ToString("yyyy-MM-dd HH:mm:ss") ?? "None"}");
        Console.WriteLine("--------------------------------------------------------------------------------");
    }
    return;
}

foreach (var regId in guids)
{
    Console.WriteLine($"\n================================================================================");
    Console.WriteLine($" REGISTRATION RECORD: {regId}");
    Console.WriteLine($"================================================================================");

    var reg = await db.RestaurantRegistrations.AsNoTracking()
        .FirstOrDefaultAsync(r => r.Id == regId);

    if (reg is null)
    {
        Console.WriteLine($"[NOT FOUND] Registration {regId} does not exist in database.");
        continue;
    }

    Console.WriteLine($"Status:               {reg.Status}");
    Console.WriteLine($"Trade Name (Ar/En):   {reg.TradeNameAr} / {reg.TradeNameEn}");
    Console.WriteLine($"Legal Name (Ar/En):   {reg.LegalNameAr} / {reg.LegalNameEn}");
    Console.WriteLine($"Commercial Reg:       {reg.CommercialRegistrationNumber} (Expires: {reg.CommercialRegistrationExpiry})");
    Console.WriteLine($"Commercial License:   {reg.CommercialLicenseNumber} (Expires: {reg.CommercialLicenseExpiry})");
    Console.WriteLine($"Food License:         {reg.FoodLicenseNumber} (Expires: {reg.FoodLicenseExpiry})");
    Console.WriteLine($"IBAN:                 {reg.Iban}");
    Console.WriteLine($"Submitted By User:    {reg.SubmittedByUserId}");
    Console.WriteLine($"Resulting Profile:    {reg.ResultingRestaurantId}");
    Console.WriteLine($"Rejection Reason:     {reg.RejectionReason ?? "(None)"}");
    Console.WriteLine($"Change Notes:         {reg.ChangeRequestNotes ?? "(None)"}");
    Console.WriteLine($"Reviewed By User:     {reg.ReviewedByUserId}");
    Console.WriteLine($"Reviewed At:          {reg.ReviewedAtUtc?.ToString("yyyy-MM-dd HH:mm:ss") ?? "(None)"}");
    Console.WriteLine($"Created At:           {reg.CreatedAtUtc:yyyy-MM-dd HH:mm:ss} UTC");

    // ── Field Reviews ─────────────────────────────────────────────────────────
    var fieldReviews = await db.RestaurantRegistrationFieldReviews.AsNoTracking()
        .Where(f => f.RegistrationId == regId)
        .OrderBy(f => f.FieldKey)
        .ToListAsync();

    Console.WriteLine($"\n[FIELD REVIEWS] ({fieldReviews.Count} fields recorded):");
    foreach (var fr in fieldReviews)
    {
        var reasonText = string.IsNullOrEmpty(fr.Reason) ? "" : $" -> Reason: {fr.Reason}";
        Console.WriteLine($"  • {fr.FieldKey,-30} [{fr.Status}]{reasonText}");
    }

    // ── Documents ─────────────────────────────────────────────────────────────
    var docs = await db.Documents.AsNoTracking()
        .Where(d => d.OwnerType == DocumentOwnerType.Registration && d.OwnerId == regId)
        .Include(d => d.Verifications)
        .OrderBy(d => d.DocumentType)
        .ToListAsync();

    Console.WriteLine($"\n[DOCUMENTS] ({docs.Count} files uploaded):");
    foreach (var doc in docs)
    {
        var latestVer = doc.Verifications.OrderByDescending(v => v.CreatedAtUtc).FirstOrDefault();
        var verStatus = latestVer?.Status.ToString() ?? "Unverified";
        var verReason = string.IsNullOrEmpty(latestVer?.Reason) ? "" : $" (Reason: {latestVer.Reason})";
        Console.WriteLine($"  • [{doc.DocumentType}] {doc.FileName} -> Status: {verStatus}{verReason}");
    }

    // ── Outbox Messages ───────────────────────────────────────────────────────
    var outbox = await db.OutboxMessages.AsNoTracking()
        .Where(o => o.AggregateId == regId.ToString())
        .OrderBy(o => o.CreatedAtUtc)
        .ToListAsync();

    Console.WriteLine($"\n[OUTBOX MESSAGES] ({outbox.Count} events dispatched):");
    foreach (var msg in outbox)
    {
        var processed = msg.ProcessedAtUtc.HasValue ? $"Processed at {msg.ProcessedAtUtc:HH:mm:ss}" : "Pending Dispatch";
        Console.WriteLine($"  • [{msg.EventType}] Created: {msg.CreatedAtUtc:HH:mm:ss} UTC | Status: {processed}");
        Console.WriteLine($"    Payload: {msg.PayloadJson}");
    }

    // ── Admin Notifications ───────────────────────────────────────────────────
    var regIdPattern = $"%{regId}%";
    var adminNotifs = await db.AdminNotifications
        .FromSqlInterpolated($"SELECT * FROM \"AdminNotifications\" WHERE \"PayloadJson\"::text LIKE {regIdPattern}")
        .AsNoTracking()
        .OrderBy(a => a.SentAtUtc)
        .ToListAsync();

    Console.WriteLine($"\n[ADMIN IN-APP NOTIFICATIONS] ({adminNotifs.Count} notifications logged):");
    foreach (var an in adminNotifs)
    {
        Console.WriteLine($"  • [{an.SentAtUtc:HH:mm:ss} UTC] {an.TitleAr} -> {an.BodyAr}");
        Console.WriteLine($"    Payload: {an.PayloadJson}");
    }

    // ── Restaurant Notifications ──────────────────────────────────────────────
    if (reg.SubmittedByUserId.HasValue)
    {
        var restNotifs = await db.RestaurantNotifications.AsNoTracking()
            .Where(rn => rn.UserId == reg.SubmittedByUserId.Value)
            .OrderBy(rn => rn.SentAtUtc)
            .ToListAsync();

        Console.WriteLine($"\n[RESTAURANT NOTIFICATIONS] for user {reg.SubmittedByUserId.Value} ({restNotifs.Count} notifications):");
        foreach (var rn in restNotifs)
        {
            Console.WriteLine($"  • [{rn.SentAtUtc:HH:mm:ss} UTC] [{rn.Type}] {rn.TitleAr} -> {rn.BodyAr}");
            Console.WriteLine($"    DeepLink: {rn.DeepLink ?? "(None)"}");
        }
    }

    // ── Restaurant Profile & Roles ────────────────────────────────────────────
    if (reg.ResultingRestaurantId.HasValue)
    {
        var profile = await db.RestaurantProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == reg.ResultingRestaurantId.Value);

        Console.WriteLine($"\n[RESULTING RESTAURANT PROFILE]:");
        if (profile is not null)
        {
            Console.WriteLine($"  • Profile ID:   {profile.Id}");
            Console.WriteLine($"  • Name:         {profile.TradeNameAr} / {profile.TradeNameEn}");
            Console.WriteLine($"  • Verification: {profile.VerificationStatus}");
            Console.WriteLine($"  • Active:       {profile.IsOperationallyActive}");
            Console.WriteLine($"  • Created At:   {profile.CreatedAtUtc:yyyy-MM-dd HH:mm:ss} UTC");
        }
        else
        {
            Console.WriteLine("  (Profile record not found)");
        }
    }
}

sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
{
    public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
