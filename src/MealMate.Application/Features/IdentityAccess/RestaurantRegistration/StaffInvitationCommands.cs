using System.Net;
using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.RestaurantRegistration;

public sealed record StaffInvitationDto(
    Guid Id,
    Guid RestaurantRegistrationId,
    string FullNameAr,
    string FullNameEn,
    string Email,
    string Phone,
    string RoleId,
    bool AllBranches,
    string InviteToken,
    string Status,
    DateTime ExpiresAtUtc,
    DateTime CreatedAtUtc);

public sealed record RestaurantStaffDto(
    string Id,
    string FullNameAr,
    string FullNameEn,
    string Email,
    string Phone,
    string RoleId,
    bool AllBranches,
    string Status,
    DateTime CreatedAtUtc,
    string? InviteToken = null,
    IReadOnlyList<string> BranchIds = null!);

public sealed record ListRestaurantStaffQuery(Guid RestaurantRegistrationId) : IQuery<IReadOnlyList<RestaurantStaffDto>>;

public sealed record ListStaffInvitationsQuery(Guid RestaurantRegistrationId) : IQuery<IReadOnlyList<StaffInvitationDto>>;

public sealed class ListStaffInvitationsQueryHandler : IQueryHandler<ListStaffInvitationsQuery, IReadOnlyList<StaffInvitationDto>>
{
    private readonly IAppDbContext _db;

    public ListStaffInvitationsQueryHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<IReadOnlyList<StaffInvitationDto>>> Handle(
        ListStaffInvitationsQuery request,
        CancellationToken cancellationToken)
    {
        var registrationIds = await ResolveRegistrationIds(request.RestaurantRegistrationId, cancellationToken);
        var invites = await _db.StaffInvitations
            .AsNoTracking()
            .Where(i => registrationIds == null || registrationIds.Contains(i.RestaurantRegistrationId))
            .OrderByDescending(i => i.CreatedAtUtc)
            .Select(i => new StaffInvitationDto(
                i.Id,
                i.RestaurantRegistrationId,
                i.FullNameAr,
                i.FullNameEn,
                i.Email,
                i.Phone,
                i.RoleId,
                i.AllBranches,
                i.InviteToken,
                i.Status,
                i.ExpiresAtUtc,
                i.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<StaffInvitationDto>>(invites);
    }

    private async Task<IReadOnlyList<Guid>?> ResolveRegistrationIds(Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            return null;
        }

        var ids = await _db.RestaurantRegistrations
            .AsNoTracking()
            .Where(r => r.Id == id || r.ResultingRestaurantId == id)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        return ids.Count > 0 ? ids : [id];
    }
}

public sealed record SendStaffInviteCommand(
    Guid RestaurantRegistrationId,
    string FullName,
    string Email,
    string Phone,
    string RoleId,
    string BranchId,
    string? PreferredLanguage = null) : ICommand<StaffInvitationDto>;

public sealed class SendStaffInviteCommandHandler : ICommandHandler<SendStaffInviteCommand, StaffInvitationDto>
{
    private readonly IAppDbContext _db;
    private readonly ITransactionalEmailSender _emailSender;

    public SendStaffInviteCommandHandler(IAppDbContext db, ITransactionalEmailSender emailSender)
    {
        _db = db;
        _emailSender = emailSender;
    }

    public async Task<Result<StaffInvitationDto>> Handle(
        SendStaffInviteCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return Error.Validation(MessageCodes.Validation.Required, "Full name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return Error.Validation(MessageCodes.Validation.Required, "Email is required.");
        }

        if (string.IsNullOrWhiteSpace(request.RoleId))
        {
            return Error.Validation(MessageCodes.Validation.Required, "Role is required.");
        }

        if (string.IsNullOrWhiteSpace(request.BranchId))
        {
            return Error.Validation(MessageCodes.Validation.Required, "Branch is required.");
        }

        var token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var invite = new StaffInvitation
        {
            RestaurantRegistrationId = request.RestaurantRegistrationId,
            FullNameAr = request.FullName,
            FullNameEn = request.FullName,
            Email = request.Email,
            Phone = request.Phone,
            RoleId = request.RoleId,
            AllBranches = false,
            BranchIdsJson = request.BranchId,
            InviteToken = token,
            Status = "Invited",
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            CreatedAtUtc = DateTime.UtcNow,
        };

        _db.StaffInvitations.Add(invite);
        await _db.SaveChangesAsync(cancellationToken);

        var recipientName = string.IsNullOrWhiteSpace(request.FullName) ? request.Email : request.FullName;
        var acceptUrl = $"{StaffInviteEmails.RestaurantDashboardBaseUrl}/restaurant/accept-invite?email={Uri.EscapeDataString(invite.Email)}&token={invite.InviteToken}";
        var language = StaffInviteEmails.ResolveLanguage(request.PreferredLanguage);
        var (subject, htmlBody, textBody) = StaffInviteEmails.Build(language, recipientName, invite.InviteToken, acceptUrl);

        var sendResult = await _emailSender.SendAsync(new TransactionalEmailRequest(
            invite.Email,
            subject,
            htmlBody,
            textBody), cancellationToken);

        if (sendResult.IsFailure)
        {
            return sendResult.Error;
        }

        var dto = new StaffInvitationDto(
            invite.Id,
            invite.RestaurantRegistrationId,
            invite.FullNameAr,
            invite.FullNameEn,
            invite.Email,
            invite.Phone,
            invite.RoleId,
            invite.AllBranches,
            invite.InviteToken,
            invite.Status,
            invite.ExpiresAtUtc,
            invite.CreatedAtUtc);

        return Result.Success(dto);
    }
}

public sealed class ListRestaurantStaffQueryHandler : IQueryHandler<ListRestaurantStaffQuery, IReadOnlyList<RestaurantStaffDto>>
{
    private readonly IAppDbContext _db;

    public ListRestaurantStaffQueryHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<IReadOnlyList<RestaurantStaffDto>>> Handle(
        ListRestaurantStaffQuery request,
        CancellationToken cancellationToken)
    {
        var staff = new List<RestaurantStaffDto>();
        var registrationIds = await ResolveRegistrationIds(request.RestaurantRegistrationId, cancellationToken);

        // 1. Add the restaurant owner from RestaurantRegistration (always safe — table always exists)
        try
        {
            var reg = await _db.RestaurantRegistrations
                .AsNoTracking()
                .Where(r => registrationIds == null || registrationIds.Contains(r.Id))
                .Select(r => new { r.Id, r.TradeNameAr, r.TradeNameEn, r.ContactPhone, r.ContactEmail, r.CreatedAtUtc })
                .FirstOrDefaultAsync(cancellationToken);

            if (reg != null)
            {
                staff.Add(new RestaurantStaffDto(
                    Id: $"owner-{reg.Id}",
                    FullNameAr: reg.TradeNameAr,
                    FullNameEn: reg.TradeNameEn ?? reg.TradeNameAr,
                    Email: reg.ContactEmail ?? "",
                    Phone: reg.ContactPhone ?? "",
                    RoleId: "owner",
                    AllBranches: true,
                    Status: "active",
                    CreatedAtUtc: reg.CreatedAtUtc,
                    BranchIds: Array.Empty<string>()));
            }
        }
        catch { /* RestaurantRegistrations should always be accessible */ }

        // 2. Add pending/accepted staff invitations (may fail if migration not yet applied)
        try
        {
            var invites = await _db.StaffInvitations
                .AsNoTracking()
                .Where(i => registrationIds == null || registrationIds.Contains(i.RestaurantRegistrationId))
                .Where(i => i.Status == "Invited" || i.Status == "Accepted")
                .OrderByDescending(i => i.CreatedAtUtc)
                .ToListAsync(cancellationToken);

            foreach (var invite in invites)
            {
                staff.Add(new RestaurantStaffDto(
                    Id: $"inv-{invite.Id}",
                    FullNameAr: invite.FullNameAr,
                    FullNameEn: invite.FullNameEn,
                    Email: invite.Email,
                    Phone: invite.Phone,
                    RoleId: invite.RoleId,
                    AllBranches: invite.AllBranches,
                    Status: invite.Status == "Accepted" ? "active" : "invited",
                    CreatedAtUtc: invite.CreatedAtUtc,
                    InviteToken: invite.InviteToken,
                    BranchIds: ParseBranchIds(invite.BranchIdsJson)));
            }
        }
        catch { /* StaffInvitations table may not exist yet — migration pending */ }

        return Result.Success<IReadOnlyList<RestaurantStaffDto>>(staff);
    }

    private static IReadOnlyList<string> ParseBranchIds(string? branchIds)
    {
        if (string.IsNullOrWhiteSpace(branchIds))
        {
            return Array.Empty<string>();
        }

        return branchIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => Guid.TryParse(value, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<IReadOnlyList<Guid>?> ResolveRegistrationIds(Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            return null;
        }

        var ids = await _db.RestaurantRegistrations
            .AsNoTracking()
            .Where(r => r.Id == id || r.ResultingRestaurantId == id)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        return ids.Count > 0 ? ids : [id];
    }
}

public sealed record AcceptStaffInviteCommand(
    string Email,
    string InviteToken,
    string Password) : ICommand;

public sealed class AcceptStaffInviteCommandHandler : ICommandHandler<AcceptStaffInviteCommand>
{
    private readonly IAppDbContext _db;
    private readonly IUserAccountService _users;

    public AcceptStaffInviteCommandHandler(IAppDbContext db, IUserAccountService users)
    {
        _db = db;
        _users = users;
    }

    public async Task<Result> Handle(AcceptStaffInviteCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return Error.Validation(MessageCodes.Validation.Required, "Email is required.");
        }

        if (string.IsNullOrWhiteSpace(request.InviteToken))
        {
            return Error.Validation(MessageCodes.Validation.Required, "Invitation token is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
        {
            return Error.Validation(MessageCodes.Validation.PasswordMinimumLength, "Password must be at least 8 characters.");
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var token = request.InviteToken.Trim();

        var invite = await _db.StaffInvitations
            .FirstOrDefaultAsync(i => i.Email.ToLower() == email && i.InviteToken == token, cancellationToken);

        if (invite is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvitationInvalid, "Invitation not found or token mismatch.");
        }

        if (!string.Equals(invite.Status, "Invited", StringComparison.OrdinalIgnoreCase))
        {
            return Error.Conflict(MessageCodes.Auth.InvitationInvalid, "Invitation is no longer active.");
        }

        if (invite.ExpiresAtUtc <= DateTime.UtcNow)
        {
            return Error.Conflict(MessageCodes.Auth.InvitationInvalid, "Invitation has expired.");
        }

        if (string.IsNullOrWhiteSpace(invite.BranchIdsJson) ||
            !Guid.TryParse(invite.BranchIdsJson.Trim(), out var branchId))
        {
            return Error.Validation(MessageCodes.Validation.Required, "Invitation branch is invalid.");
        }

        var restaurantId = await (
            from branch in _db.RestaurantBranches.AsNoTracking()
            join registration in _db.RestaurantRegistrations.AsNoTracking()
                on branch.RestaurantRegistrationId equals registration.Id
            where branch.Id == branchId
            select registration.ResultingRestaurantId)
            .FirstOrDefaultAsync(cancellationToken);

        // The current branch list can synthesize the primary branch from the
        // registration while no RestaurantBranch row exists yet. In that case,
        // the branch id is the RestaurantRegistration id.
        if (restaurantId is null)
        {
            restaurantId = await _db.RestaurantRegistrations
                .AsNoTracking()
                .Where(registration => registration.Id == branchId)
                .Select(registration => registration.ResultingRestaurantId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (restaurantId is null)
        {
            return Error.Conflict(MessageCodes.Domain.Generic, "Invitation restaurant is not active yet.");
        }

        // Reuse an existing account (e.g. a previous attempt) or create one.
        var existing = await _users.FindByEmailAsync(email, cancellationToken);
        Guid userId;
        if (existing is not null)
        {
            userId = existing.Id;
            if (existing.RestaurantId != restaurantId.Value)
            {
                await _users.SetRestaurantIdAsync(userId, restaurantId.Value, cancellationToken);
            }
        }
        else
        {
            var create = await _users.CreateAsync(new CreateUserRequest(
                email,
                string.IsNullOrWhiteSpace(invite.Phone) ? null : invite.Phone.Trim(),
                request.Password,
                string.IsNullOrWhiteSpace(invite.FullNameAr) ? invite.FullNameEn : invite.FullNameAr,
                UserType.Restaurant,
                AccountStatus.Active,
                restaurantId.Value), cancellationToken);

            if (create.IsFailure)
            {
                return create.Error;
            }

            userId = create.Value.Id;
            await _users.SetEmailConfirmedAsync(userId, true, cancellationToken);
        }

        // Best-effort mirror of the invite's role onto the ASP.NET Identity roles when
        // the role is a dynamic (GUID) restaurant role. Never fail acceptance if the
        // dynamic role naming is not appropriate for the underlying store.
        if (Guid.TryParse(invite.RoleId, out _))
        {
            try
            {
                var ensure = await _users.EnsureRoleExistsAsync(invite.RoleId, null, cancellationToken);
                if (ensure.IsSuccess)
                {
                    await _users.AddToRoleAsync(userId, invite.RoleId, cancellationToken);
                }
            }
            catch
            {
                // Ignore — the application-level BranchStaffAssignment.RoleId still carries the role.
            }
        }

        _db.BranchStaffAssignments.Add(new BranchStaffAssignment
        {
            RestaurantBranchId = branchId,
            UserId = userId,
            RoleId = invite.RoleId,
            IsBranchManager = false,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
        });

        invite.Status = "Accepted";
        invite.ModifiedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

public sealed record ResendStaffInviteCommand(
    Guid InviteId,
    string? PreferredLanguage = null) : ICommand<StaffInvitationDto>;

public sealed class ResendStaffInviteCommandHandler : ICommandHandler<ResendStaffInviteCommand, StaffInvitationDto>
{
    private readonly IAppDbContext _db;
    private readonly ITransactionalEmailSender _emailSender;

    public ResendStaffInviteCommandHandler(IAppDbContext db, ITransactionalEmailSender emailSender)
    {
        _db = db;
        _emailSender = emailSender;
    }

    public async Task<Result<StaffInvitationDto>> Handle(
        ResendStaffInviteCommand request,
        CancellationToken cancellationToken)
    {
        var invite = await _db.StaffInvitations
            .FirstOrDefaultAsync(i => i.Id == request.InviteId, cancellationToken);

        if (invite is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvitationInvalid, "Invitation not found.");
        }

        if (!string.Equals(invite.Status, "Invited", StringComparison.OrdinalIgnoreCase))
        {
            return Error.Conflict(MessageCodes.Auth.InvitationInvalid, "Only pending invites can be resent.");
        }

        var token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        invite.InviteToken = token;
        invite.ExpiresAtUtc = DateTime.UtcNow.AddDays(7);
        invite.ModifiedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        var recipientName = string.IsNullOrWhiteSpace(invite.FullNameAr) ? invite.Email : invite.FullNameAr;
        var acceptUrl = $"{StaffInviteEmails.RestaurantDashboardBaseUrl}/restaurant/accept-invite?email={Uri.EscapeDataString(invite.Email)}&token={invite.InviteToken}";
        var language = StaffInviteEmails.ResolveLanguage(request.PreferredLanguage);
        var (subject, htmlBody, textBody) = StaffInviteEmails.Build(language, recipientName, invite.InviteToken, acceptUrl);

        var sendResult = await _emailSender.SendAsync(new TransactionalEmailRequest(
            invite.Email,
            subject,
            htmlBody,
            textBody), cancellationToken);

        if (sendResult.IsFailure)
        {
            return sendResult.Error;
        }

        return new StaffInvitationDto(
            invite.Id,
            invite.RestaurantRegistrationId,
            invite.FullNameAr,
            invite.FullNameEn,
            invite.Email,
            invite.Phone,
            invite.RoleId,
            invite.AllBranches,
            invite.InviteToken,
            invite.Status,
            invite.ExpiresAtUtc,
            invite.CreatedAtUtc);
    }
}

/// <summary>
/// Renders the staff invitation email in a single language — Arabic-only when the
/// preferred language is Arabic, English-only otherwise. The link points to the
/// restaurant dashboard accept-invite page.
/// </summary>
internal static class StaffInviteEmails
{
    // Local/dev default — replace with a configured restaurant dashboard URL when available.
    internal const string RestaurantDashboardBaseUrl = "http://localhost:4201";

    /// <summary>Resolves the preferred language, defaulting to Arabic when absent.</summary>
    internal static string ResolveLanguage(string? preferredLanguage)
        => string.IsNullOrWhiteSpace(preferredLanguage)
            ? SupportedCultures.Arabic
            : EmailLanguage.Resolve(preferredLanguage);

    internal static (string Subject, string Html, string Text) Build(
        string language,
        string recipientName,
        string token,
        string acceptUrl)
    {
        var safeName = WebUtility.HtmlEncode(recipientName);
        var safeUrl = WebUtility.HtmlEncode(acceptUrl);

        if (language.Equals(SupportedCultures.Arabic, StringComparison.OrdinalIgnoreCase))
        {
            var html = $"""
                <div dir="rtl" style="font-family:'Segoe UI',Arial,sans-serif;color:#102a24;line-height:1.7">
                  <h2 style="color:#087a5b;margin-top:0">مرحبًا {safeName}،</h2>
                  <p>تمت دعوتك للانضمام إلى فريق المطعم على منصة MealMate. لإنشاء حسابك وقبول الدعوة، اضغط على الزر أدناه:</p>
                  <p style="margin:18px 0"><a href="{safeUrl}" style="background:#087a5b;color:#fff;text-decoration:none;padding:12px 20px;border-radius:10px;font-weight:700;display:inline-block">قبول الدعوة وإنشاء الحساب</a></p>
                  <p>إذا لم يعمل الزر، انسخ الرابط التالي والصقه في المتصفح:</p>
                  <p dir="ltr" style="background:#eefaf6;padding:10px;border-radius:8px;font-size:12px;word-break:break-all">{safeUrl}</p>
                  <p>الدعوة صالحة لمدة 7 أيام.</p>
                </div>
                """;
            var text = $"تمت دعوتك للانضمام إلى فريق المطعم على منصة MealMate.\nلقبول الدعوة وإنشاء حسابك، افتح الرابط التالي:\n{acceptUrl}\nالدعوة صالحة لمدة 7 أيام.";
            return ("دعوة للانضمام إلى فريق المطعم", html, text);
        }

        var enHtml = $"""
            <div dir="ltr" style="font-family:Arial,'Segoe UI',sans-serif;color:#102a24;line-height:1.7">
              <h2 style="color:#087a5b;margin-top:0">Hi {safeName},</h2>
              <p>You have been invited to join the restaurant team on MealMate. To create your account and accept the invite, click the button below:</p>
              <p style="margin:18px 0"><a href="{safeUrl}" style="background:#087a5b;color:#fff;text-decoration:none;padding:12px 20px;border-radius:10px;font-weight:700;display:inline-block">Accept invite &amp; create account</a></p>
              <p>If the button does not work, copy and paste this link into your browser:</p>
              <p dir="ltr" style="background:#eefaf6;padding:10px;border-radius:8px;font-size:12px;word-break:break-all">{safeUrl}</p>
              <p>The invite expires in 7 days.</p>
            </div>
            """;
        var enText = $"You have been invited to join the restaurant team on MealMate.\nTo accept the invite and create your account, open the link below:\n{acceptUrl}\nThe invite expires in 7 days.";
        return ("Restaurant staff invitation", enHtml, enText);
    }
}
