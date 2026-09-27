using MealMate.Application.Common.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public class OrphanPickupPhotoCleanupService
{
    private readonly IAppDbContext _db;
    private readonly IFileStorage _storage;
    private readonly ILogger<OrphanPickupPhotoCleanupService> _logger;

    public OrphanPickupPhotoCleanupService(
        IAppDbContext db,
        IFileStorage storage,
        ILogger<OrphanPickupPhotoCleanupService> logger)
    {
        _db = db;
        _storage = storage;
        _logger = logger;
    }

    public async Task<int> RunCleanupOnceAsync(CancellationToken cancellationToken = default)
    {
        var cutoffUtc = DateTime.UtcNow.AddHours(-24);

        var orphans = await _db.PickupConditionPhotos
            .Where(p => p.ConfirmedAtUtc == null &&
                        p.DeletedAtUtc == null &&
                        p.UploadedAtUtc < cutoffUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        int count = 0;
        foreach (var photo in orphans)
        {
            try
            {
                await _storage.DeleteAsync(photo.StorageKey, cancellationToken);
                photo.DeletedAtUtc = DateTime.UtcNow;
                count++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete orphan condition photo {StorageKey}", photo.StorageKey);
            }
        }

        if (count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return count;
    }
}
