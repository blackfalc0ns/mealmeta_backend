using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace MealMate.Api.Security;

/// <summary>Issues short-lived capabilities for one private driver evidence file.</summary>
public sealed class DriverEvidenceReadLinks(IDataProtectionProvider protection, TimeProvider clock)
{
    private const string ContentPath = "/api/v1/uploads/content";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    public string? CreateUrl(string? value)
    {
        var key = ExtractPrivateKey(value);
        if (key is null) return value;
        var token = Protector(key).Protect(clock.GetUtcNow().Add(Lifetime).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        return QueryHelpers.AddQueryString(ContentPath, new Dictionary<string, string?> { ["key"] = key, ["readToken"] = token });
    }

    public bool Validate(string key, string? token)
    {
        if (!IsPrivateKey(key) || string.IsNullOrEmpty(token) || token.Length > 8192) return false;
        try
        {
            return long.TryParse(Protector(key).Unprotect(token), NumberStyles.None, CultureInfo.InvariantCulture, out var expires)
                && expires > clock.GetUtcNow().ToUnixTimeSeconds();
        }
        catch (CryptographicException) { return false; }
        catch (FormatException) { return false; }
        catch (ArgumentException) { return false; }
    }

    private IDataProtector Protector(string key)
        => protection.CreateProtector("MealMate.DriverEvidenceReadLinks.v1", key);

    private static string? ExtractPrivateKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (IsPrivateKey(value)) return value;
        if (!value.StartsWith(ContentPath + "?", StringComparison.Ordinal) || value.Contains('#')) return null;
        var query = QueryHelpers.ParseQuery(value[ContentPath.Length..]);
        if (query.Any(x => x.Key is not ("key" or "readToken")) || !query.TryGetValue("key", out var keys) || keys.Count != 1) return null;
        return IsPrivateKey(keys[0]) ? keys[0] : null;
    }

    private static bool IsPrivateKey(string? key)
        => !string.IsNullOrEmpty(key) && key.Length <= 500
            && (key.StartsWith("uploads/drivers/issue-evidence/", StringComparison.Ordinal)
                || key.StartsWith("uploads/drivers/delivery/", StringComparison.Ordinal))
            && !key.Any(c => char.IsControl(c) || c is '\\' or '%' or '?' or '#' or ':')
            && key.Split('/').All(segment => segment.Length > 0 && segment is not ("." or ".."));
}
