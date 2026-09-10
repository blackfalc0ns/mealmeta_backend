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
            bank = new InfluencerBankInfoDto(profile.AccountHolderName ?? profile.DisplayName, profile.BankName, profile.Iban);
        }

        var rate = profile.DefaultCommissionRate?.AsPercent ?? 10m;

        return new InfluencerPartnerProfileDto(
            profile.Id,
            profile.DisplayName,
            profile.InfluencerCode ?? string.Empty,
            rate,
            profile.IsActive,
            profile.HasCompletedSocialOnboarding,
            bank,
            social);
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

    public async Task<Result> UpdateSocialLinksAsync(Guid userId, UpdateInfluencerSocialLinksCommand command, CancellationToken ct = default)
    {
        var profile = await _db.InfluencerProfiles
            .FirstOrDefaultAsync(x => (x.UserId == userId || x.LinkedCustomerUserId == userId) && x.IsActive, ct);

        if (profile is null)
        {
            return Error.NotFound(MessageCodes.Influencers.ProfileNotFound);
        }

        var socialDto = new InfluencerSocialLinksDto(
            command.Tiktok,
            command.Instagram,
            command.Whatsapp,
            command.Telegram,
            command.Youtube,
            command.Snapchat);

        profile.SocialLinksJson = JsonSerializer.Serialize(socialDto);
        profile.HasCompletedSocialOnboarding = true;

        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
