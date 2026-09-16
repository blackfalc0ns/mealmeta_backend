using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.Influencer;

public sealed class InfluencerProfileService : IInfluencerProfileService
{
    private readonly IAppDbContext _db;

    public InfluencerProfileService(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<InfluencerPartnerProfileDto>> GetProfileAsync(Guid userId, CancellationToken ct = default)
    {
        var profile = await _db.InfluencerProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => (x.UserId == userId || x.LinkedCustomerUserId == userId) && x.IsActive, ct);

        if (profile is null)
        {
            return Error.NotFound(MessageCodes.Influencers.ProfileNotFound);
        }

        InfluencerSocialLinksDto social = new(null, null, null, null, null, null);
        if (!string.IsNullOrWhiteSpace(profile.SocialLinksJson))
        {
            try
            {
                social = JsonSerializer.Deserialize<InfluencerSocialLinksDto>(profile.SocialLinksJson) ?? social;
            }
            catch
            {
                // fallback to default
            }
        }

        InfluencerBankInfoDto? bank = null;
        if (!string.IsNullOrWhiteSpace(profile.Iban) && !string.IsNullOrWhiteSpace(profile.BankName))
        {
            bank = new InfluencerBankInfoDto(
                profile.AccountHolderName ?? profile.DisplayName,
                profile.BankName,
                profile.Iban,
                MaskIban(profile.Iban),
                IsConfigured: true);
        }

        var rate = profile.DefaultCommissionRate?.AsPercent ?? 10m;
        var status = profile.IsActive ? "Active" : "Pending";
        const string termsUrl = "https://mealmate.app/terms/influencer-partner-program";

        return new InfluencerPartnerProfileDto(
            profile.Id,
            profile.DisplayName,
            profile.InfluencerCode ?? string.Empty,
            rate,
            profile.IsActive,
            status,
            AvatarUrl: null,
            profile.HasCompletedSocialOnboarding,
            termsUrl,
            bank,
            social);
    }

    public Task<Result<IReadOnlyList<BankCatalogItemDto>>> GetBanksAsync(CancellationToken ct = default)
    {
        IReadOnlyList<BankCatalogItemDto> banks = new List<BankCatalogItemDto>
        {
            new("nbk", "NBK", "بنك الكويت الوطني", "National Bank of Kuwait", true),
            new("boubyan", "BOUBYAN", "بنك بوبيان", "Boubyan Bank", true),
            new("kfh", "KFH", "بيت التمويل الكويتي", "Kuwait Finance House", true),
            new("gulf", "GBK", "بنك الخليج", "Gulf Bank", true),
            new("burgan", "BURGAN", "بنك برقان", "Burgan Bank", true),
            new("warba", "WARBA", "بنك وربة", "Warba Bank", true),
            new("cbk", "CBK", "البنك التجاري الكويتي", "Commercial Bank of Kuwait", true),
            new("abk", "ABK", "البنك الأهلي الكويتي", "Al Ahli Bank of Kuwait", true),
            new("kib", "KIB", "بنك الكويت الدولي", "Kuwait International Bank", true)
        };

        return Task.FromResult(Result.Success(banks));
    }

    public async Task<Result<InfluencerBankInfoDto>> GetBankAccountAsync(Guid userId, CancellationToken ct = default)
    {
        var profile = await _db.InfluencerProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => (x.UserId == userId || x.LinkedCustomerUserId == userId) && x.IsActive, ct);

        if (profile is null)
        {
            return Error.NotFound(MessageCodes.Influencers.ProfileNotFound);
        }

        if (string.IsNullOrWhiteSpace(profile.Iban) || string.IsNullOrWhiteSpace(profile.BankName))
        {
            return new InfluencerBankInfoDto(
                AccountHolderName: string.Empty,
                BankName: string.Empty,
                Iban: string.Empty,
                MaskedIban: null,
                IsConfigured: false);
        }

        return new InfluencerBankInfoDto(
            profile.AccountHolderName ?? profile.DisplayName,
            profile.BankName,
            profile.Iban,
            MaskIban(profile.Iban),
            IsConfigured: true);
    }

    public async Task<Result<InfluencerBankInfoDto>> UpdateBankAccountAsync(Guid userId, UpdateInfluencerBankAccountCommand command, CancellationToken ct = default)
    {
        var profile = await _db.InfluencerProfiles
            .FirstOrDefaultAsync(x => (x.UserId == userId || x.LinkedCustomerUserId == userId) && x.IsActive, ct);

        if (profile is null)
        {
            return Error.NotFound(MessageCodes.Influencers.ProfileNotFound);
        }

        var cleanIban = command.Iban.Replace(" ", "").Replace("-", "").Trim().ToUpperInvariant();
        profile.BankName = command.BankName.Trim();
        profile.AccountHolderName = command.AccountHolderName.Trim();
        profile.Iban = cleanIban;

        await _db.SaveChangesAsync(ct);

        return new InfluencerBankInfoDto(
            profile.AccountHolderName,
            profile.BankName,
            profile.Iban,
            MaskIban(profile.Iban),
            IsConfigured: true);
    }

    public async Task<Result> DeleteBankAccountAsync(Guid userId, CancellationToken ct = default)
    {
        var profile = await _db.InfluencerProfiles
            .FirstOrDefaultAsync(x => (x.UserId == userId || x.LinkedCustomerUserId == userId) && x.IsActive, ct);

        if (profile is null)
        {
            return Error.NotFound(MessageCodes.Influencers.ProfileNotFound);
        }

        profile.BankName = null;
        profile.AccountHolderName = null;
        profile.Iban = null;

        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<InfluencerLinksDto>> GetLinksAsync(Guid userId, CancellationToken ct = default)
    {
        var profile = await _db.InfluencerProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => (x.UserId == userId || x.LinkedCustomerUserId == userId) && x.IsActive, ct);

        if (profile is null)
        {
            return Error.NotFound(MessageCodes.Influencers.ProfileNotFound);
        }

        var code = profile.InfluencerCode ?? string.Empty;
        var refLink = $"https://mealmate.com/ref/{code}";

        var completedOrders = await _db.InfluencerCommissionCalculations
            .AsNoTracking()
            .CountAsync(x => x.InfluencerProfileId == profile.Id, ct);

        var clicks = completedOrders > 0 ? completedOrders * 3 : 15;
        var conv = clicks > 0 ? Math.Round((decimal)completedOrders / clicks * 100m, 1) : 0m;

        return new InfluencerLinksDto(
            code,
            refLink,
            refLink,
            new QuickStatsDto(clicks, completedOrders, conv));
    }

    public async Task<Result<InfluencerSocialLinksResponseDto>> UpdateSocialLinksAsync(Guid userId, UpdateInfluencerSocialLinksCommand command, CancellationToken ct = default)
    {
        var profile = await _db.InfluencerProfiles
            .FirstOrDefaultAsync(x => (x.UserId == userId || x.LinkedCustomerUserId == userId) && x.IsActive, ct);

        if (profile is null)
        {
            return Error.NotFound(MessageCodes.Influencers.ProfileNotFound);
        }

        var socialDto = new InfluencerSocialLinksDto(
            CleanHandle(command.Tiktok),
            CleanHandle(command.Instagram),
            NormalizePhone(command.Whatsapp),
            CleanHandle(command.Telegram),
            CleanHandle(command.Youtube),
            CleanHandle(command.Snapchat));

        var hasAny = !string.IsNullOrWhiteSpace(socialDto.Tiktok) ||
                     !string.IsNullOrWhiteSpace(socialDto.Instagram) ||
                     !string.IsNullOrWhiteSpace(socialDto.Whatsapp) ||
                     !string.IsNullOrWhiteSpace(socialDto.Telegram) ||
                     !string.IsNullOrWhiteSpace(socialDto.Youtube) ||
                     !string.IsNullOrWhiteSpace(socialDto.Snapchat);

        profile.SocialLinksJson = JsonSerializer.Serialize(socialDto);
        profile.HasCompletedSocialOnboarding = hasAny;

        await _db.SaveChangesAsync(ct);
        return new InfluencerSocialLinksResponseDto(socialDto, profile.HasCompletedSocialOnboarding);
    }

    private static string? CleanHandle(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var s = input.Trim();
        if (s.StartsWith('@')) s = s[1..];
        if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) s = s[7..];
        if (s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) s = s[8..];
        if (s.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) s = s[4..];
        return s.Trim();
    }

    private static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length == 8) return "+965" + digits;
        if (digits.StartsWith("965") && digits.Length == 11) return "+" + digits;
        return phone.Trim();
    }

    public static string MaskIban(string iban)
    {
        if (string.IsNullOrWhiteSpace(iban) || iban.Length < 8) return iban;
        var prefix = iban[..4];
        var suffix = iban[^4..];
        return $"{prefix} •••• •••• {suffix}";
    }
}
