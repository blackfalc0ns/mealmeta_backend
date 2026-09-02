namespace MealMate.Infrastructure.Push;

public static class FirebaseCredentialPathResolver
{
    public static string? Resolve(string? configuredPath, string? contentRoot, string? environmentPath)
    {
        foreach (var candidate in new[] { configuredPath, environmentPath })
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            if (Path.IsPathRooted(candidate) && File.Exists(candidate))
                return Path.GetFullPath(candidate);

            foreach (var root in Roots(contentRoot))
            {
                var dir = new DirectoryInfo(root);
                while (dir is not null)
                {
                    var combined = Path.GetFullPath(Path.Combine(dir.FullName, candidate));
                    if (File.Exists(combined))
                        return combined;

                    dir = dir.Parent;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> Roots(string? contentRoot)
    {
        if (!string.IsNullOrWhiteSpace(contentRoot))
            yield return contentRoot;

        yield return AppContext.BaseDirectory;
        yield return Directory.GetCurrentDirectory();
    }
}
