using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Common.Abstractions;

/// <summary>
/// Application-facing database surface for Identity & Access and shared Foundation tables.
/// Implemented by <c>AppDbContext</c> in Persistence.
/// </summary>
public interface IAppDbContext
{
    DbSet<Permission> Permissions { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<AccessScope> AccessScopes { get; }
    DbSet<UserScope> UserScopes { get; }
    DbSet<CustomerProfile> CustomerProfiles { get; }
    DbSet<RestaurantProfile> RestaurantProfiles { get; }
    DbSet<DriverProfile> DriverProfiles { get; }
    DbSet<InfluencerProfile> InfluencerProfiles { get; }
    DbSet<RestaurantRegistration> RestaurantRegistrations { get; }
    DbSet<DriverRegistration> DriverRegistrations { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<OtpChallenge> OtpChallenges { get; }
    DbSet<Document> Documents { get; }
    DbSet<DocumentVerification> DocumentVerifications { get; }
    DbSet<IdempotencyRecord> IdempotencyRecords { get; }
}
