using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MealMate.Persistence.Seeding;

public static class CatalogImagesSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("CatalogImagesSeeder");

        var imageRoot = ResolveImageRoot();
        if (imageRoot is null)
        {
            throw new DirectoryNotFoundException(
                "Catalog images folder was not found. Expected .tmp-catalog-images next to the backend repo.");
        }

        var uploaded = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        async Task<string> UploadAsync(string fileName, string folder)
        {
            if (uploaded.TryGetValue(fileName, out var existing))
            {
                return existing;
            }

            var path = Path.Combine(imageRoot, fileName);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Missing catalog image {fileName}", path);
            }

            await using var stream = File.OpenRead(path);
            var stored = await storage.SaveAsync(stream, fileName, "image/jpeg", folder, cancellationToken);
            var key = CatalogImageStorage.Normalize(stored.StorageKey) ?? stored.StorageKey;
            uploaded[fileName] = key;
            logger.LogInformation("Uploaded {File} -> {Key}", fileName, key);
            return key;
        }

        var programs = await db.Programs.ToListAsync(cancellationToken);
        foreach (var program in programs)
        {
            if (!ProgramFiles.TryGetValue(program.Code, out var fileName))
            {
                continue;
            }

            program.ImageStorageKey = await UploadAsync(fileName, CatalogImageStorage.ProgramPrefix.TrimEnd('/'));
        }

        var bundles = await db.Bundles.ToListAsync(cancellationToken);
        foreach (var bundle in bundles)
        {
            var fileName = BundleFiles.TryGetValue(bundle.Code, out var mapped)
                ? mapped
                : FileForSlots(bundle.BreakfastCount, bundle.LunchCount, bundle.DinnerCount, bundle.SnackCount);
            if (fileName is null)
            {
                continue;
            }

            bundle.ImageStorageKey = await UploadAsync(fileName, CatalogImageStorage.BundlePrefix.TrimEnd('/'));
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Attached catalog images to {Programs} programs and {Bundles} bundles.",
            programs.Count(x => !string.IsNullOrWhiteSpace(x.ImageStorageKey)),
            bundles.Count(x => !string.IsNullOrWhiteSpace(x.ImageStorageKey)));
    }

    private static string? ResolveImageRoot()
    {
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".tmp-catalog-images")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".tmp-catalog-images")),
            @"F:\MealMate\backend\.tmp-catalog-images",
        };

        return candidates.FirstOrDefault(Directory.Exists);
    }

    private static string? FileForSlots(int breakfast, int lunch, int dinner, int snack)
        => (breakfast, lunch, dinner, snack) switch
        {
            (1, 1, 1, 2) => "pack-bld2s.jpg",
            (1, 1, 0, 1) => "pack-bl1s.jpg",
            (0, 1, 1, 2) => "pack-ld2s.jpg",
            (1, 1, 1, 1) => "pack-full-day.jpg",
            (1, 1, 1, 0) => "pack-three-meals.jpg",
            (0, 1, 1, 1) => "pack-standard.jpg",
            (0, 1, 0, 1) => "pack-lite.jpg",
            (0, 1, 1, 0) => "pack-two-meals.jpg",
            _ => snack > 0 && breakfast + lunch + dinner >= 3 ? "pack-full-day.jpg"
                : breakfast + lunch + dinner >= 3 ? "pack-three-meals.jpg"
                : breakfast + lunch + dinner == 2 && snack > 0 ? "pack-standard.jpg"
                : breakfast + lunch + dinner == 2 ? "pack-two-meals.jpg"
                : "pack-lite.jpg",
        };

    private static readonly Dictionary<string, string> ProgramFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PROG_WEIGHT_LOSS"] = "prog-weight-loss.jpg",
        ["PROG_BALANCED"] = "prog-lifestyle.jpg",
        ["PROG_ATHLETE"] = "prog-athletic.jpg",
        ["PROG_MUSCLE_GAIN"] = "prog-muscle-gain.jpg",
        ["PROG_KETO"] = "prog-keto.jpg",
        ["PROG_LOW_CARB"] = "prog-low-carb.jpg",
    };

    private static readonly Dictionary<string, string> BundleFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BND_KETO_FULL"] = "pack-full-day.jpg",
        ["BND_KETO_LITE"] = "pack-lite.jpg",
        ["BND_KETO_STANDARD"] = "pack-standard.jpg",
        ["BND_BALANCED_STD"] = "pack-two-meals.jpg",
        ["BND_BALANCED_LITE"] = "pack-lite.jpg",
        ["BND_BALANCED_FULL"] = "pack-full-day.jpg",
        ["BND_ATHLETE_MAX"] = "pack-three-meals.jpg",
        ["BND_ATHLETE_STANDARD"] = "pack-standard.jpg",
        ["BND_ATHLETE_ELITE"] = "pack-full-day.jpg",
        ["PKG-BL1S"] = "pack-bl1s.jpg",
        ["PKG-BL1S-LC"] = "pack-bl1s.jpg",
        ["PKG-LD2S"] = "pack-ld2s.jpg",
        ["PKG-BLD2S"] = "pack-bld2s.jpg",
        ["PKG-BLD2S-MG"] = "pack-bld2s.jpg",
    };
}
