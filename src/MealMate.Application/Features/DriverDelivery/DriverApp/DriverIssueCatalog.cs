namespace MealMate.Application.Features.DriverDelivery.DriverApp;

/// <summary>
/// Fixed machine-value catalogs for the driver screens 08.01 (report issue) and
/// 08.02 (request reassignment). Every accepted request value must come from
/// these tables; free text is carried by the optional description/notes fields
/// only. Machine values are stable API identifiers — the Arabic/English labels
/// are display strings for docs and composed titles, never sent by the client.
/// Issue-type values reuse the dispatcher support categories where they overlap
/// (CustomerUnavailable, AddressIssue, DamagedBox, SevereDelay, VehicleBreakdown)
/// so driver reports land in the support center under known categories.
/// </summary>
public static class DriverIssueCatalog
{
    public sealed record IssueTypeValue(string Value, string LabelAr, string LabelEn);

    /// <summary>Design options of screen 08.01. Lookups are case-insensitive.</summary>
    public static readonly IReadOnlyList<IssueTypeValue> IssueTypes =
    [
        new("CustomerUnavailable", "العميل لا يرد / غير متاح", "Customer not responding / unavailable"),
        new("AddressIssue", "عنوان خاطئ أو غير واضح", "Incorrect or unclear address"),
        new("DamagedBox", "بوكس تالف", "Damaged box"),
        new("SevereDelay", "تأخير شديد", "Severe delay"),
        new("CustomerRefusal", "العميل رفض الاستلام", "Customer refused to receive"),
        new("VehicleBreakdown", "عطل بالمركبة", "Vehicle breakdown"),
        new("TrafficAccident", "حادث سير / طارئ", "Traffic accident / emergency"),
        new("SevereWeather", "سوء الأحوال الجوية", "Severe weather"),
        new("Other", "مشكلة أخرى", "Other issue"),
    ];

    public sealed record ReasonValue(string Value, string LabelAr, string LabelEn);

    /// <summary>Design dropdown options of screen 08.02. Lookups are case-insensitive.</summary>
    public static readonly IReadOnlyList<ReasonValue> ReassignmentReasons =
    [
        new("VehicleBreakdown", "عطل بالمركبة", "Vehicle breakdown"),
        new("TrafficAccident", "حادث سير مروري", "Traffic accident"),
        new("MedicalOrPersonalEmergency", "ظرف صحي أو طارئ شخصي", "Medical or personal emergency"),
        new("DeviceFailure", "نفاد بطارية الهاتف أو عطل في التطبيق", "Device failure"),
        new("OtherOperationalReason", "سبب تشغيلي آخر", "Other operational reason"),
    ];

    /// <summary>Accepted severities for a reported issue; absent severity defaults to Medium.</summary>
    public static readonly IReadOnlyList<string> Severities = ["Low", "Medium", "High"];

    public static string? NormalizeIssueType(string? value)
        => Match(IssueTypes.Select(t => t.Value), value);

    public static string? NormalizeSeverity(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? "Medium"
            : Match(Severities, value);

    public static string? NormalizeReassignmentReason(string? value)
        => Match(ReassignmentReasons.Select(r => r.Value), value);

    public static (string LabelAr, string LabelEn) DescribeIssueType(string value)
        => Describe(IssueTypes.Select(t => (t.Value, t.LabelAr, t.LabelEn)), value);

    public static (string LabelAr, string LabelEn) DescribeReassignmentReason(string value)
        => Describe(ReassignmentReasons.Select(r => (r.Value, r.LabelAr, r.LabelEn)), value);

    private static string? Match(IEnumerable<string> allowed, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return allowed.FirstOrDefault(a => a.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static (string LabelAr, string LabelEn) Describe(
        IEnumerable<(string Value, string LabelAr, string LabelEn)> values, string value)
    {
        foreach (var (machineValue, labelAr, labelEn) in values)
        {
            if (machineValue.Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                return (labelAr, labelEn);
            }
        }

        return (value, value);
    }
}
