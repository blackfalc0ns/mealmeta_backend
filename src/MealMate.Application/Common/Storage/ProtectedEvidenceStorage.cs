namespace MealMate.Application.Common.Storage;

public static class ProtectedEvidenceStorage
{
    public static bool IsProtectedKey(string key)
    {
        var normalized = key.Replace('\\', '/');
        return normalized.StartsWith("uploads/drivers/delivery/", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("uploads/drivers/delivery", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("uploads/drivers/issue-evidence/", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("uploads/drivers/issue-evidence", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("documents/support/evidence/", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("documents/support/evidence", StringComparison.OrdinalIgnoreCase);
    }

    public static string ProtectedLocalFolder(string key)
    {
        var normalized = key.Replace('\\', '/');
        if (normalized.StartsWith("uploads/drivers/delivery/", StringComparison.OrdinalIgnoreCase)) return "uploads/drivers/delivery";
        if (normalized.StartsWith("uploads/drivers/issue-evidence/", StringComparison.OrdinalIgnoreCase)) return "uploads/drivers/issue-evidence";
        if (normalized.StartsWith("documents/support/evidence/", StringComparison.OrdinalIgnoreCase)) return "documents/support/evidence";
        throw new InvalidOperationException("Storage key is not protected evidence.");
    }
}
