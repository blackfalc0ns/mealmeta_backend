using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.IdentityAccess;

/// <summary>
/// Tracks an admin invitation through its lifecycle: Pending → Accepted | Revoked | Expired.
/// The invitation is the source of truth for role/scope until the invitee accepts and a real
/// User account is created. No User/UserRole/UserScope rows exist while the invitation is pending.
/// </summary>
public sealed class AdminInvitation : AuditableEntity
{
    /// <summary>Lowercased, trimmed email address of the invitee.</summary>
    public string NormalizedEmail { get; set; } = default!;

    /// <summary>Display name captured at invite time.</summary>
    public string FullName { get; set; } = default!;

    /// <summary>Role name to assign when the invitation is accepted (e.g. SuperAdmin, OperationsAdmin).</summary>
    public string RoleName { get; set; } = default!;

    /// <summary>Optional access scope to stage on acceptance.</summary>
    public Guid? AccessScopeId { get; set; }

    /// <summary>Navigation to the optional access scope.</summary>
    public AccessScope? AccessScope { get; set; }

    /// <summary>User ID of the admin who issued the invitation.</summary>
    public Guid InvitedByUserId { get; set; }

    /// <summary>Business justification captured at invite time.</summary>
    public string Reason { get; set; } = default!;

    /// <summary>Current lifecycle status.</summary>
    public AdminInvitationStatus Status { get; set; } = AdminInvitationStatus.Pending;

    /// <summary>When the invitation was first created.</summary>
    public DateTime InvitedAtUtc { get; set; }

    /// <summary>When the invitation was accepted (if applicable).</summary>
    public DateTime? AcceptedAtUtc { get; set; }

    /// <summary>When the invitation was revoked (if applicable).</summary>
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>Reason captured when revoking the invitation.</summary>
    public string? RevokedReason { get; set; }

    /// <summary>User ID of the admin who revoked the invitation (if applicable).</summary>
    public Guid? RevokedByUserId { get; set; }

    /// <summary>The User ID created on acceptance (null while pending/revoked).</summary>
    public Guid? AcceptedUserId { get; set; }
}
