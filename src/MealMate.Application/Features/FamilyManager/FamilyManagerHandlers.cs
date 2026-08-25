using System.Security.Cryptography;
using System.Text;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.FamilyManager;

public sealed class FamilyManagerHandlers :
    IQueryHandler<GetFamilyHomeQuery, FamilyHomeDto>,
    IQueryHandler<GetFamilyMembersQuery, IReadOnlyList<FamilyMemberDetailDto>>,
    IQueryHandler<GetFamilyMemberQuery, FamilyMemberDetailDto>,
    ICommandHandler<CreateFamilyMemberCommand, FamilyMemberDetailDto>,
    IQueryHandler<GetFamilyMemberAccessQuery, FamilyMemberAccessDto>,
    ICommandHandler<UpdateFamilyMemberAccessCommand, FamilyMemberAccessDto>,
    IQueryHandler<GetFamilyMemberPermissionsQuery, FamilyMemberPermissionsDto>,
    ICommandHandler<UpdateFamilyMemberPermissionsCommand, FamilyMemberPermissionsDto>,
    ICommandHandler<AcceptFamilyInviteCommand, FamilyMemberAccessDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IFamilyPricingSettingsReader _pricing;

    public FamilyManagerHandlers(
        IAppDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        IFamilyPricingSettingsReader pricing)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _pricing = pricing;
    }

    public async Task<Result<FamilyHomeDto>> Handle(GetFamilyHomeQuery request, CancellationToken cancellationToken)
    {
        var groupResult = await RequireActiveManagerGroupAsync(cancellationToken);
        if (groupResult.IsFailure) return groupResult.Error;
        var group = groupResult.Value;

        var members = await ActiveMembersQuery(group.Id).Include(x => x.MemberSubscription).ThenInclude(s => s!.Program)
            .OrderBy(x => x.Role == FamilyMemberRole.Manager ? 0 : 1)
            .ThenBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);

        var accessByMember = await _db.FamilyMemberAccesses.AsNoTracking()
            .Where(x => members.Select(m => m.Id).Contains(x.FamilyMemberId))
            .ToDictionaryAsync(x => x.FamilyMemberId, cancellationToken);

        var today = _clock.TodayUtc;
        var subIds = members.Where(m => m.MemberSubscriptionId is not null).Select(m => m.MemberSubscriptionId!.Value).ToList();
        var todayDays = await _db.SubscriptionDays.AsNoTracking()
            .Where(d => subIds.Contains(d.SubscriptionId) && d.ServiceDate == today)
            .ToListAsync(cancellationToken);
        var dayIds = todayDays.Select(d => d.Id).ToList();
        var filledByDay = await _db.DailyMealSelections.AsNoTracking()
            .Where(s => dayIds.Contains(s.SubscriptionDayId))
            .GroupBy(s => s.SubscriptionDayId)
            .Select(g => new { DayId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DayId, x => x.Count, cancellationToken);

        var package = await ResolvePackageAsync(group, cancellationToken);
        var bundleIds = members.Where(m => m.BundleId != null).Select(m => m.BundleId!.Value).Distinct().ToList();
        var bundles = await _db.Bundles.AsNoTracking()
            .Where(b => bundleIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, cancellationToken);
        var homeMembers = members.Select(m =>
        {
            accessByMember.TryGetValue(m.Id, out var access);
            var day = m.MemberSubscriptionId is { } sid
                ? todayDays.FirstOrDefault(d => d.SubscriptionId == sid)
                : null;
            int? filled = day is not null && filledByDay.TryGetValue(day.Id, out var c) ? c : null;
            int? required = null;
            if (m.BundleId is { } bid && bundles.TryGetValue(bid, out var bundle))
                required = bundle.BreakfastCount + bundle.LunchCount + bundle.DinnerCount + bundle.SnackCount;
            return new FamilyHomeMemberDto(
                m.Id, m.DisplayName, m.Role, access?.IsPaused == true, m.MemberSubscriptionId,
                m.MemberSubscription?.Program?.NameAr, m.MemberSubscription?.Program?.NameEn,
                day?.Status.ToString(), filled, required);
        }).ToList();

        var shortcuts = new List<FamilyShortcutDto>
        {
            new("addresses", false, "wave2"),
            new("subscriptions", false, "wave2"),
            new("payments", false, "wave3"),
            new("reports", false, "wave3")
        };

        return Result.Success(new FamilyHomeDto(
            group.Id,
            group.Name,
            group.Status,
            package?.NameAr,
            package?.NameEn,
            group.QuotedFinalPrice,
            group.CurrencyCode,
            homeMembers,
            homeMembers,
            shortcuts));
    }

    public async Task<Result<IReadOnlyList<FamilyMemberDetailDto>>> Handle(GetFamilyMembersQuery request, CancellationToken cancellationToken)
    {
        var groupResult = await RequireActiveManagerGroupAsync(cancellationToken);
        if (groupResult.IsFailure) return groupResult.Error;
        var members = await ActiveMembersQuery(groupResult.Value.Id)
            .Include(x => x.MemberSubscription).ThenInclude(s => s!.Program)
            .OrderBy(x => x.Role == FamilyMemberRole.Manager ? 0 : 1)
            .ThenBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);
        var access = await _db.FamilyMemberAccesses.AsNoTracking()
            .Where(x => members.Select(m => m.Id).Contains(x.FamilyMemberId))
            .ToDictionaryAsync(x => x.FamilyMemberId, cancellationToken);
        var programIds = members.Where(m => m.ProgramId != null).Select(m => m.ProgramId!.Value).Distinct().ToList();
        var programs = await _db.Programs.AsNoTracking()
            .Where(p => programIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        IReadOnlyList<FamilyMemberDetailDto> list = members.Select(m => MapDetail(m, access.GetValueOrDefault(m.Id), programs)).ToList();
        return Result.Success(list);
    }

    public async Task<Result<FamilyMemberDetailDto>> Handle(GetFamilyMemberQuery request, CancellationToken cancellationToken)
    {
        var loaded = await LoadManagedMemberAsync(request.MemberId, cancellationToken);
        if (loaded.IsFailure) return loaded.Error;
        var (member, access) = loaded.Value;
        var program = member.ProgramId is { } pid
            ? await _db.Programs.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pid, cancellationToken)
            : null;
        var programs = program is null ? new Dictionary<Guid, Program>() : new Dictionary<Guid, Program> { [program.Id] = program };
        return Result.Success(MapDetail(member, access, programs));
    }

    public async Task<Result<FamilyMemberDetailDto>> Handle(CreateFamilyMemberCommand request, CancellationToken cancellationToken)
    {
        var groupResult = await RequireActiveManagerGroupAsync(cancellationToken);
        if (groupResult.IsFailure) return groupResult.Error;
        var group = groupResult.Value;

        // Wave 1: adding seats after payment requires payment flow (Wave 2/3).
        return Error.Conflict(MessageCodes.Family.MemberRequiresPayment);
    }

    public async Task<Result<FamilyMemberAccessDto>> Handle(GetFamilyMemberAccessQuery request, CancellationToken cancellationToken)
    {
        var loaded = await LoadManagedMemberAsync(request.MemberId, cancellationToken);
        if (loaded.IsFailure) return loaded.Error;
        var access = await EnsureAccessAsync(loaded.Value.Member, cancellationToken);
        return Result.Success(MapAccess(access));
    }

    public async Task<Result<FamilyMemberAccessDto>> Handle(UpdateFamilyMemberAccessCommand request, CancellationToken cancellationToken)
    {
        var loaded = await LoadManagedMemberAsync(request.MemberId, cancellationToken);
        if (loaded.IsFailure) return loaded.Error;
        var (member, _) = loaded.Value;
        var access = await EnsureAccessAsync(member, cancellationToken);

        if (request.Request.Remove == true)
        {
            if (member.Role == FamilyMemberRole.Manager)
                return Error.Conflict(MessageCodes.Family.CannotRemoveManager);
            access.RemovedAtUtc = _clock.UtcNow;
            access.IsPaused = true;
            access.PausedAtUtc ??= _clock.UtcNow;
        }

        if (request.Request.IsPaused is { } paused)
        {
            access.IsPaused = paused;
            access.PausedAtUtc = paused ? _clock.UtcNow : null;
        }

        if (request.Request.HasSeparateAccount is { } separate)
        {
            access.HasSeparateAccount = separate;
            if (separate && access.LinkedCustomerUserId is null)
            {
                var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                    .TrimEnd('=').Replace('+', '-').Replace('/', '_');
                access.InviteTokenHash = HashToken(raw);
                access.InviteExpiresAtUtc = _clock.UtcNow.AddDays(7);
                // Store raw token temporarily in InviteDeepLink mapping only for response
                await _db.SaveChangesAsync(cancellationToken);
                return Result.Success(MapAccess(access, inviteRawToken: raw));
            }

            if (!separate)
            {
                access.InviteTokenHash = null;
                access.InviteExpiresAtUtc = null;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(MapAccess(access));
    }

    public async Task<Result<FamilyMemberPermissionsDto>> Handle(GetFamilyMemberPermissionsQuery request, CancellationToken cancellationToken)
    {
        var loaded = await LoadManagedMemberAsync(request.MemberId, cancellationToken);
        if (loaded.IsFailure) return loaded.Error;
        var perms = await EnsurePermissionsAsync(loaded.Value.Member, cancellationToken);
        return Result.Success(MapPermissions(perms));
    }

    public async Task<Result<FamilyMemberPermissionsDto>> Handle(UpdateFamilyMemberPermissionsCommand request, CancellationToken cancellationToken)
    {
        var loaded = await LoadManagedMemberAsync(request.MemberId, cancellationToken);
        if (loaded.IsFailure) return loaded.Error;
        if (loaded.Value.Member.Role == FamilyMemberRole.Manager)
            return Error.Conflict(MessageCodes.Family.ManagerPermissionsImmutable);

        var perms = await EnsurePermissionsAsync(loaded.Value.Member, cancellationToken);
        perms.EditDailyMeals = request.Request.EditDailyMeals;
        perms.ChangeDeliveryTime = request.Request.ChangeDeliveryTime;
        perms.ViewProgram = request.Request.ViewProgram;
        perms.ChangeProgram = request.Request.ChangeProgram;
        perms.EditMealsAndDays = request.Request.EditMealsAndDays;
        perms.EditAddress = request.Request.EditAddress;
        perms.UnitExchange = request.Request.UnitExchange;
        perms.ChangePassword = request.Request.ChangePassword;
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(MapPermissions(perms));
    }

    public async Task<Result<FamilyMemberAccessDto>> Handle(AcceptFamilyInviteCommand request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserType != UserType.Customer || _currentUser.UserId is null)
            return Error.Forbidden(MessageCodes.Auth.Forbidden);

        var hash = HashToken(request.Token.Trim());
        var access = await _db.FamilyMemberAccesses
            .Include(x => x.FamilyMember)
            .FirstOrDefaultAsync(x => x.InviteTokenHash == hash, cancellationToken);
        if (access is null || access.InviteExpiresAtUtc is null || access.InviteExpiresAtUtc < _clock.UtcNow)
            return Error.Validation(MessageCodes.Family.InviteInvalid);
        if (access.RemovedAtUtc is not null)
            return Error.Conflict(MessageCodes.Family.InviteConflict);
        if (access.LinkedCustomerUserId is not null && access.LinkedCustomerUserId != _currentUser.UserId)
            return Error.Conflict(MessageCodes.Family.InviteConflict);

        var alreadyLinked = await _db.FamilyMemberAccesses.AsNoTracking()
            .AnyAsync(x => x.LinkedCustomerUserId == _currentUser.UserId && x.Id != access.Id && x.RemovedAtUtc == null, cancellationToken);
        if (alreadyLinked)
            return Error.Conflict(MessageCodes.Family.InviteConflict);

        access.LinkedCustomerUserId = _currentUser.UserId;
        access.HasSeparateAccount = true;
        access.InviteTokenHash = null;
        access.InviteExpiresAtUtc = null;
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(MapAccess(access));
    }

    private async Task<Result<FamilyGroup>> RequireActiveManagerGroupAsync(CancellationToken ct)
    {
        if (_currentUser.UserType != UserType.Customer || _currentUser.UserId is null)
            return Error.Forbidden(MessageCodes.Auth.Forbidden);

        var group = await _db.FamilyGroups
            .FirstOrDefaultAsync(x => x.ManagerCustomerUserId == _currentUser.UserId && x.Status == FamilyGroupStatus.Active, ct);
        if (group is null)
            return Error.Forbidden(MessageCodes.Family.NotManager);
        return Result.Success(group);
    }

    private IQueryable<FamilyMember> ActiveMembersQuery(Guid groupId)
        => _db.FamilyMembers
            .Where(m => m.FamilyGroupId == groupId)
            .Where(m => !_db.FamilyMemberAccesses.Any(a => a.FamilyMemberId == m.Id && a.RemovedAtUtc != null));

    private async Task<Result<(FamilyMember Member, FamilyMemberAccess? Access)>> LoadManagedMemberAsync(Guid memberId, CancellationToken ct)
    {
        var groupResult = await RequireActiveManagerGroupAsync(ct);
        if (groupResult.IsFailure) return groupResult.Error;

        var member = await _db.FamilyMembers
            .Include(x => x.MemberSubscription).ThenInclude(s => s!.Program)
            .FirstOrDefaultAsync(x => x.Id == memberId && x.FamilyGroupId == groupResult.Value.Id, ct);
        if (member is null) return Error.NotFound(MessageCodes.Family.MemberNotFound);

        var access = await _db.FamilyMemberAccesses.FirstOrDefaultAsync(x => x.FamilyMemberId == member.Id, ct);
        if (access?.RemovedAtUtc is not null) return Error.NotFound(MessageCodes.Family.MemberNotFound);
        return Result.Success((member, access));
    }

    private async Task<FamilyMemberAccess> EnsureAccessAsync(FamilyMember member, CancellationToken ct)
    {
        var access = await _db.FamilyMemberAccesses.FirstOrDefaultAsync(x => x.FamilyMemberId == member.Id, ct);
        if (access is not null) return access;
        access = new FamilyMemberAccess { FamilyMemberId = member.Id };
        _db.FamilyMemberAccesses.Add(access);
        await _db.SaveChangesAsync(ct);
        return access;
    }

    private async Task<FamilyMemberPermissions> EnsurePermissionsAsync(FamilyMember member, CancellationToken ct)
    {
        var perms = await _db.FamilyMemberPermissions.FirstOrDefaultAsync(x => x.FamilyMemberId == member.Id, ct);
        if (perms is not null) return perms;
        perms = new FamilyMemberPermissions { FamilyMemberId = member.Id };
        _db.FamilyMemberPermissions.Add(perms);
        await _db.SaveChangesAsync(ct);
        return perms;
    }

    private async Task<ProgramsSubscriptions.FamilySizePackageSettings?> ResolvePackageAsync(FamilyGroup group, CancellationToken ct)
    {
        var settings = await _pricing.GetAsync(ct);
        var resolved = FamilyPackageResolver.ResolveForGroup(settings, group.SelectedFamilyPackageId);
        return resolved.IsSuccess ? resolved.Value : null;
    }

    private static FamilyMemberDetailDto MapDetail(
        FamilyMember m,
        FamilyMemberAccess? access,
        IReadOnlyDictionary<Guid, Program> programs)
    {
        programs.TryGetValue(m.ProgramId ?? Guid.Empty, out var program);
        return new FamilyMemberDetailDto(
            m.Id, m.DisplayName, m.Role, m.RelationshipCode, m.Gender, m.AgeYears,
            m.NutritionalGoalCode, m.ActivityLevel, m.SetupStatus,
            m.ProgramId, m.BundleId, m.SelectedTier, m.DurationDays, m.IncludeFriday,
            m.MemberSubscriptionId, program?.NameAr, program?.NameEn,
            access?.IsPaused == true, access?.HasSeparateAccount == true, access?.LinkedCustomerUserId);
    }

    private FamilyMemberAccessDto MapAccess(FamilyMemberAccess access, string? inviteRawToken = null)
        => new(
            access.FamilyMemberId,
            access.HasSeparateAccount,
            access.LinkedCustomerUserId,
            access.IsPaused,
            access.PausedAtUtc,
            access.RemovedAtUtc,
            inviteRawToken is null ? null : $"mealmate://family/invite/{inviteRawToken}",
            access.InviteExpiresAtUtc);

    private static FamilyMemberPermissionsDto MapPermissions(FamilyMemberPermissions p)
        => new(p.FamilyMemberId, p.EditDailyMeals, p.ChangeDeliveryTime, p.ViewProgram, p.ChangeProgram,
            p.EditMealsAndDays, p.EditAddress, p.UnitExchange, p.ChangePassword);

    private static string HashToken(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }
}
