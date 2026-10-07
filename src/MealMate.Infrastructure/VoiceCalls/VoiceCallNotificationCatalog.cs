using System.Globalization;
using System.Resources;

namespace MealMate.Infrastructure.VoiceCalls;

/// <summary>Legacy visible notifications use the persisted recipient locale; unset locale follows customer Arabic default.</summary>
public static class VoiceCallNotificationCatalog
{
    private static readonly ResourceManager Resources = new("MealMate.Infrastructure.VoiceCalls.Resources.VoiceCallNotifications", typeof(VoiceCallNotificationCatalog).Assembly);
    public static (string Title, string Body) ForEvent(string eventKey, string? language)
    {
        var key = eventKey switch { "IncomingCall" => "Incoming", "CallCancelled" => "Cancelled", "CallMissed" => "Missed", _ => throw new ArgumentOutOfRangeException(nameof(eventKey)) };
        var culture = CultureInfo.GetCultureInfo(string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "ar");
        return (Read(key + "Title", culture), Read(key + "Body", culture));
    }
    private static string Read(string key, CultureInfo culture) => Resources.GetString(key, culture)
        ?? throw new MissingManifestResourceException("Missing voice-call notification translation: " + key);
}
