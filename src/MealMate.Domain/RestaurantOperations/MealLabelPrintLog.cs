using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

public sealed class MealLabelPrintLog : AuditableEntity
{
    public Guid RestaurantId { get; set; }

    public Guid? OrderId { get; set; }

    public Guid? MealId { get; set; }

    public Guid TemplateId { get; set; }

    public int TemplateVersion { get; set; }

    public Guid PrintedByUserId { get; set; }

    public DateTime PrintedAtUtc { get; set; }

    public string? ClientIp { get; set; }

    public string PrintStatus { get; set; } = "Success";

    public string? Notes { get; set; }
}
