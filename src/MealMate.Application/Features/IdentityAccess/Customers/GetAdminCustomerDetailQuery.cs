using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.Customers;

public sealed record AdminCustomerDetailDto(
    Guid Id,
    string? FullName,
    string? Email,
    string? PhoneNumber,
    AccountStatus AccountStatus,
    bool EmailConfirmed,
    bool PhoneNumberConfirmed,
    bool TwoFactorEnabled,
    DateTime CreatedAtUtc,
    DateTime? LastLoginAtUtc,
    string? PreferredLanguage,
    DateOnly? DateOfBirth,
    bool OnboardingCompleted,
    string? ProfileImageStorageKey,
    string? DietaryPattern,
    string? DietaryNotes,
    IReadOnlyList<AdminCustomerAllergyDto> Allergies,
    IReadOnlyList<string> DislikedIngredients,
    IReadOnlyList<string> DietaryPreferences,
    IReadOnlyList<AdminCustomerAddressDto> Addresses,
    AdminCustomerSubscriptionDto? ActiveSubscription,
    IReadOnlyList<AdminCustomerSubscriptionDto> Subscriptions,
    IReadOnlyList<AdminCustomerFreezeRequestDto> FreezeRequests,
    AdminCustomerFamilyGroupDto? FamilyGroup,
    decimal CashWalletBalanceKd,
    decimal PromotionalWalletBalanceKd,
    IReadOnlyList<AdminCustomerWalletTxDto> WalletTransactions,
    IReadOnlyList<AdminCustomerPaymentReceiptDto> Payments,
    IReadOnlyList<AdminCustomerDeviceDto> Devices,
    IReadOnlyList<AdminCustomerPushLogDto> PushDeliveryLogs,
    int TotalOrders,
    int ComplaintsCount);

public sealed record AdminCustomerAddressDto(
    Guid Id,
    string Label,
    string AddressLine,
    string? Governorate,
    string? Area,
    string? Block,
    string? Street,
    string? Avenue,
    string? Building,
    string? Unit,
    string? Floor,
    decimal? Latitude,
    decimal? Longitude,
    string? Notes,
    bool IsDefault,
    bool NotifyOnDelivery,
    bool IsActiveDelivery);

public sealed record AdminCustomerAllergyDto(
    string Code,
    string NameAr,
    string NameEn,
    string? Severity);

public sealed record AdminCustomerSubscriptionDto(
    Guid Id,
    Guid ProgramId,
    string ProgramNameAr,
    string ProgramNameEn,
    Guid BundleId,
    string BundleNameAr,
    string BundleNameEn,
    RestaurantTier Tier,
    string TierNameAr,
    string TierNameEn,
    SubscriptionStatus Status,
    string StatusNameAr,
    string StatusNameEn,
    DateOnly StartDate,
    DateOnly EndDate,
    int DurationDays,
    int UsedDays,
    int RemainingDays,
    decimal PriceKd,
    bool IncludeFriday,
    string? PaymentInvoiceId,
    DateTime? PaymentVerifiedAtUtc,
    DateTime CreatedAtUtc);

public sealed record AdminCustomerFreezeRequestDto(
    Guid Id,
    Guid SubscriptionId,
    DateOnly StartDate,
    DateOnly EndDate,
    int Days,
    string? Reason,
    string Status,
    DateTime CreatedAtUtc);

public sealed record AdminCustomerFamilyGroupDto(
    Guid GroupId,
    string NameAr,
    string NameEn,
    bool IsManager,
    Guid ManagerCustomerId,
    string? ManagerName,
    IReadOnlyList<AdminCustomerFamilyMemberSummaryDto> Members);

public sealed record AdminCustomerFamilyMemberSummaryDto(
    Guid CustomerId,
    string FullName,
    string Role,
    string Status,
    int QuotaDays,
    int UsedDays);

public sealed record AdminCustomerWalletTxDto(
    Guid Id,
    WalletType WalletType,
    WalletTransactionType Type,
    decimal AmountKd,
    decimal BalanceAfterKd,
    string? ReferenceType,
    Guid? ReferenceId,
    string? Description,
    DateTime OccurredAtUtc);

public sealed record AdminCustomerPaymentReceiptDto(
    Guid Id,
    string? PaymentMethod,
    string? InvoiceReference,
    decimal AmountKd,
    string Status,
    DateTime? PaidAtUtc);

public sealed record AdminCustomerDeviceDto(
    Guid Id,
    string Token,
    MobilePlatform Platform,
    string? DeviceId,
    bool IsActive,
    DateTime LastSeenAtUtc);

public sealed record AdminCustomerPushLogDto(
    Guid Id,
    string EventKey,
    Guid SubjectId,
    DateTime SentAtUtc);

public sealed record GetAdminCustomerDetailQuery(Guid CustomerUserId) : IQuery<AdminCustomerDetailDto>;

public sealed class GetAdminCustomerDetailQueryHandler : IQueryHandler<GetAdminCustomerDetailQuery, AdminCustomerDetailDto>
{
    private readonly IUserAccountService _users;
    private readonly IAppDbContext _db;
    private readonly ITotpAuthenticatorService _totp;

    public GetAdminCustomerDetailQueryHandler(
        IUserAccountService users,
        IAppDbContext db,
        ITotpAuthenticatorService totp)
    {
        _users = users;
        _db = db;
        _totp = totp;
    }

    public async Task<Result<AdminCustomerDetailDto>> Handle(GetAdminCustomerDetailQuery request, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(request.CustomerUserId, ct);
        if (user is null)
        {
            return Result.Failure<AdminCustomerDetailDto>(Error.NotFound("customer.not_found", $"Customer {request.CustomerUserId} was not found."));
        }

        var twoFactorEnabled = await _totp.IsEnabledAsync(user.Id, ct);
        var profile = await _db.CustomerProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == user.Id, ct);

        // Saved Addresses
        var addresses = await _db.CustomerAddresses.AsNoTracking()
            .Where(x => x.UserId == user.Id)
            .OrderByDescending(x => x.IsDefault)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => new AdminCustomerAddressDto(
                x.Id,
                x.Label,
                x.AddressLine,
                x.Governorate,
                x.Area,
                x.Block,
                x.Street,
                x.Avenue,
                x.Building,
                x.Unit,
                x.Floor,
                x.Latitude,
                x.Longitude,
                x.Notes,
                x.IsDefault,
                x.NotifyOnDelivery,
                profile != null && profile.ActiveDeliveryAddressId == x.Id))
            .ToListAsync(ct);

        // Food Preferences
        var allAllergens = await _db.AllergenDefinitions.AsNoTracking().ToListAsync(ct);
        var allergenMap = allAllergens.ToDictionary(a => a.Code, StringComparer.OrdinalIgnoreCase);

        var customerAllergiesRaw = await _db.CustomerAllergyPreferences.AsNoTracking()
            .Where(x => x.CustomerUserId == user.Id)
            .ToListAsync(ct);

        var allergies = customerAllergiesRaw
            .Where(x => !x.IsDislike)
            .Select(x =>
            {
                allergenMap.TryGetValue(x.AllergenCode, out var def);
                return new AdminCustomerAllergyDto(
                    x.AllergenCode,
                    def?.NameAr ?? x.AllergenCode,
                    def?.NameEn ?? x.AllergenCode,
                    x.Severity);
            })
            .ToList();

        var dislikedIngredients = customerAllergiesRaw
            .Where(x => x.IsDislike)
            .Select(x =>
            {
                allergenMap.TryGetValue(x.AllergenCode, out var def);
                return def?.NameAr ?? def?.NameEn ?? x.AllergenCode;
            })
            .Distinct()
            .ToList();

        var dietaryPreferences = await _db.CustomerDietaryPreferences.AsNoTracking()
            .Where(x => x.CustomerUserId == user.Id)
            .Select(x => x.PreferenceCode)
            .ToListAsync(ct);

        // Subscriptions
        var subsRaw = await _db.Subscriptions.AsNoTracking()
            .Include(x => x.Program)
            .Include(x => x.Bundle)
            .Where(x => x.CustomerUserId == user.Id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        var subIds = subsRaw.Select(s => s.Id).ToList();

        // Calculate used days for each subscription
        var usedDaysPerSub = new Dictionary<Guid, int>();
        if (subIds.Count > 0)
        {
            var now = DateOnly.FromDateTime(DateTime.UtcNow);
            var daysGrouped = await _db.SubscriptionDays.AsNoTracking()
                .Where(d => subIds.Contains(d.SubscriptionId) && (d.Status == SubscriptionDayStatus.Delivered || d.ServiceDate < now))
                .GroupBy(d => d.SubscriptionId)
                .Select(g => new { SubId = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            foreach (var item in daysGrouped)
            {
                usedDaysPerSub[item.SubId] = item.Count;
            }
        }

        var subscriptions = subsRaw.Select(s =>
        {
            var used = usedDaysPerSub.TryGetValue(s.Id, out var u) ? u : 0;
            var remaining = Math.Max(0, s.DurationDays - used);

            var (tierAr, tierEn) = s.SelectedTier switch
            {
                RestaurantTier.Basic => ("أساسي", "Basic"),
                RestaurantTier.Platinum => ("بلاتينيوم", "Platinum"),
                RestaurantTier.Elite => ("إيليت", "Elite"),
                _ => ("افتراضي", "Default")
            };

            var (statusAr, statusEn) = s.Status switch
            {
                SubscriptionStatus.Active => ("نشط", "Active"),
                SubscriptionStatus.Scheduled => ("مجدول", "Scheduled"),
                SubscriptionStatus.Frozen => ("مجمّد", "Frozen"),
                SubscriptionStatus.Cancelled => ("ملغي", "Cancelled"),
                SubscriptionStatus.PendingPayment => ("بانتظار الدفع", "Pending Payment"),
                SubscriptionStatus.Draft => ("مسودة", "Draft"),
                SubscriptionStatus.Completed => ("مكتمل", "Completed"),
                SubscriptionStatus.Expired => ("منتهي", "Expired"),
                _ => (s.Status.ToString(), s.Status.ToString())
            };

            return new AdminCustomerSubscriptionDto(
                s.Id,
                s.ProgramId,
                s.Program?.NameAr ?? s.Program?.NameEn ?? "برنامج غذائي",
                s.Program?.NameEn ?? s.Program?.NameAr ?? "Diet Program",
                s.BundleId,
                s.Bundle?.NameAr ?? s.Bundle?.NameEn ?? "باقة وجبات",
                s.Bundle?.NameEn ?? s.Bundle?.NameAr ?? "Meal Bundle",
                s.SelectedTier,
                tierAr,
                tierEn,
                s.Status,
                statusAr,
                statusEn,
                s.StartDate,
                s.EndDate,
                s.DurationDays,
                used,
                remaining,
                s.PaidAmount?.Amount ?? 0m,
                s.IncludeFriday,
                s.PaymentInvoiceId,
                s.PaymentVerifiedAtUtc,
                s.CreatedAtUtc);
        }).ToList();

        var activeSubscription = subscriptions.FirstOrDefault(s => s.Status is SubscriptionStatus.Active or SubscriptionStatus.Scheduled or SubscriptionStatus.Frozen)
            ?? subscriptions.FirstOrDefault();

        // Freeze Requests
        var freezeRequests = subIds.Count == 0
            ? new List<AdminCustomerFreezeRequestDto>()
            : await _db.SubscriptionFreezeRequests.AsNoTracking()
                .Where(f => subIds.Contains(f.SubscriptionId))
                .OrderByDescending(f => f.CreatedAtUtc)
                .Select(f => new AdminCustomerFreezeRequestDto(
                    f.Id,
                    f.SubscriptionId,
                    f.FreezeStartDate,
                    f.FreezeEndDate,
                    f.FreezeDays,
                    f.Reason,
                    f.Status.ToString(),
                    f.CreatedAtUtc))
                .ToListAsync(ct);

        // Family Group
        AdminCustomerFamilyGroupDto? familyGroup = null;
        var familySub = await _db.FamilySubscriptions.AsNoTracking()
            .Include(f => f.PrimarySubscription)
            .FirstOrDefaultAsync(f => f.MemberCustomerUserId == user.Id || (f.PrimarySubscription != null && f.PrimarySubscription.CustomerUserId == user.Id), ct);

        if (familySub != null)
        {
            var isManager = familySub.PrimarySubscription != null && familySub.PrimarySubscription.CustomerUserId == user.Id;
            var managerId = familySub.PrimarySubscription?.CustomerUserId ?? familySub.MemberCustomerUserId;
            var managerUser = await _users.FindByIdAsync(managerId, ct);

            var membersList = new List<AdminCustomerFamilyMemberSummaryDto>
            {
                new(managerId, managerUser?.FullName ?? managerUser?.Email ?? "المدير", "Manager", "Active", 30, 0),
                new(familySub.MemberCustomerUserId, familySub.DisplayName ?? "عضو عائلي", "Member", "Active", 30, 0)
            };

            familyGroup = new AdminCustomerFamilyGroupDto(
                familySub.Id,
                "مجموعة " + (managerUser?.FullName ?? "العائلة"),
                (managerUser?.FullName ?? "Family") + " Group",
                isManager,
                managerId,
                managerUser?.FullName ?? managerUser?.Email,
                membersList);
        }

        // Wallets
        var cashWallet = await _db.CustomerCashWallets.AsNoTracking()
            .FirstOrDefaultAsync(w => w.CustomerUserId == user.Id, ct);
        var promoWallet = await _db.CustomerPromotionalWallets.AsNoTracking()
            .FirstOrDefaultAsync(w => w.CustomerUserId == user.Id, ct);

        var walletTransactions = await _db.WalletTransactions.AsNoTracking()
            .Where(t => t.CustomerUserId == user.Id)
            .OrderByDescending(t => t.OccurredAtUtc)
            .Take(50)
            .Select(t => new AdminCustomerWalletTxDto(
                t.Id,
                t.WalletType,
                t.Type,
                t.Amount != null ? t.Amount.Amount : 0m,
                t.BalanceAfter != null ? t.BalanceAfter.Amount : 0m,
                t.ReferenceType,
                t.ReferenceId,
                t.Description,
                t.OccurredAtUtc))
            .ToListAsync(ct);

        // Payment Receipts / Invoices
        var payments = await _db.SubscriptionPayments.AsNoTracking()
            .Where(p => p.CustomerUserId == user.Id)
            .OrderByDescending(p => p.CreatedAtUtc)
            .Take(30)
            .Select(p => new AdminCustomerPaymentReceiptDto(
                p.Id,
                p.GatewayCode,
                p.GatewayTransactionId,
                p.Amount != null ? p.Amount.Amount : 0m,
                p.Status.ToString(),
                p.PaidAtUtc))
            .ToListAsync(ct);

        // Devices
        var devices = await _db.CustomerDeviceTokens.AsNoTracking()
            .Where(d => d.UserId == user.Id)
            .OrderByDescending(d => d.LastSeenAtUtc)
            .Select(d => new AdminCustomerDeviceDto(
                d.Id,
                d.Token,
                d.Platform,
                d.DeviceId,
                d.IsActive,
                d.LastSeenAtUtc))
            .ToListAsync(ct);

        // Push Logs
        var pushLogs = await _db.CustomerPushDeliveryLogs.AsNoTracking()
            .Where(l => l.UserId == user.Id)
            .OrderByDescending(l => l.SentAtUtc)
            .Take(25)
            .Select(l => new AdminCustomerPushLogDto(
                l.Id,
                l.EventKey,
                l.SubjectId,
                l.SentAtUtc))
            .ToListAsync(ct);

        // Metrics
        var totalOrders = subIds.Count == 0
            ? 0
            : await _db.SubscriptionDays.AsNoTracking()
                .CountAsync(d => subIds.Contains(d.SubscriptionId) && d.Status == SubscriptionDayStatus.Delivered, ct);

        var complaintsCount = await _db.Complaints.AsNoTracking()
            .CountAsync(c => c.CustomerUserId == user.Id, ct);

        return new AdminCustomerDetailDto(
            user.Id,
            user.FullName ?? user.Email,
            user.Email,
            user.PhoneNumber,
            user.AccountStatus,
            user.EmailConfirmed,
            user.PhoneNumberConfirmed,
            twoFactorEnabled,
            user.CreatedAtUtc,
            user.LastLoginAtUtc,
            profile?.PreferredLanguage ?? "ar",
            profile?.DateOfBirth,
            profile?.OnboardingCompleted ?? false,
            profile?.ProfileImageStorageKey,
            profile?.DietaryPattern,
            profile?.DietaryNotes,
            allergies,
            dislikedIngredients,
            dietaryPreferences,
            addresses,
            activeSubscription,
            subscriptions,
            freezeRequests,
            familyGroup,
            cashWallet?.Balance?.Amount ?? 0m,
            promoWallet?.Balance?.Amount ?? 0m,
            walletTransactions,
            payments,
            devices,
            pushLogs,
            totalOrders,
            complaintsCount);
    }
}
