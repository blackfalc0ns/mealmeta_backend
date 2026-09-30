using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MealMate.Application.Common.Abstractions;

namespace MealMate.Application.Common.Storage;

public static class RegistrationStorageValidation
{
    public static async Task<bool> AllFilesExistAndHaveContentAsync(
        IFileStorage? storage,
        IEnumerable<string?> storageKeys,
        CancellationToken cancellationToken)
    {
        var keys = storageKeys.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray();
        if (keys.Length == 0) return true;
        if (storage is null) return false;

        foreach (var key in keys)
        {
            try
            {
                await using var stream = await storage.OpenReadAsync(key!, cancellationToken);
                if (stream is null) return false;
                if (stream.CanSeek)
                {
                    if (stream.Length == 0) return false;
                }
                else if (await stream.ReadAsync(new byte[1], cancellationToken) == 0)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        return true;
    }
}
