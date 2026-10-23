using MealMate.Domain.Common;

namespace MealMate.Domain.ComplaintsSupport;

public enum SupportCaseMessageVisibility { Participant, Internal }

public sealed class SupportCaseMessage : BaseEntity
{
    public Guid SupportCaseId { get; set; }
    public Guid? SupportCaseTaskId { get; set; }
    public Guid AuthorUserId { get; set; }
    public SupportCaseMessageVisibility Visibility { get; set; }
    public string Body { get; set; } = default!;
    public string[] AttachmentKeys { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; }
}
