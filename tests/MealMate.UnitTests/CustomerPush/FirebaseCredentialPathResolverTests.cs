using MealMate.Infrastructure.Push;

namespace MealMate.UnitTests.CustomerPush;

public sealed class FirebaseCredentialPathResolverTests
{
    [Fact]
    public void Resolves_relative_path_from_content_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "mealmate-fcm-" + Guid.NewGuid().ToString("N"));
        var secrets = Path.Combine(root, "secrets");
        Directory.CreateDirectory(secrets);
        var file = Path.Combine(secrets, "firebase-service-account.json");
        File.WriteAllText(file, "{}");

        try
        {
            var resolved = FirebaseCredentialPathResolver.Resolve(
                "secrets/firebase-service-account.json",
                contentRoot: root,
                environmentPath: null);

            Assert.Equal(Path.GetFullPath(file), resolved);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Falls_back_to_environment_path()
    {
        var root = Path.Combine(Path.GetTempPath(), "mealmate-fcm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "env.json");
        File.WriteAllText(file, "{}");

        try
        {
            var resolved = FirebaseCredentialPathResolver.Resolve(
                "missing/path.json",
                contentRoot: root,
                environmentPath: file);

            Assert.Equal(Path.GetFullPath(file), resolved);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
