namespace MealMate.Infrastructure.VoiceCalls;

public sealed class CoturnOptions
{
    public const string SectionName = "Coturn";

    public bool Enabled { get; set; } = true;

    public string Realm { get; set; } = "turn.mealmate.app";

    public string ServerHost { get; set; } = "turn.mealmate.app";

    public int StunPort { get; set; } = 3478;

    public int TurnPort { get; set; } = 3478;

    public int TurnsPort { get; set; } = 5349;

    public string SharedSecret { get; set; } = "mealmate_coturn_secret_key_change_in_production";

    public int TtlSeconds { get; set; } = 3600;

    public bool EnableTls { get; set; } = true;

    /// <summary>
    /// Explicit flag indicating whether a valid TLS certificate and private key are configured and verified on Coturn.
    /// When false, TURNS (TLS over TCP) URLs will not be advertised to clients even if EnableTls is true.
    /// </summary>
    public bool TlsCertConfigured { get; set; } = true;

    /// <summary>
    /// Optional file path to certificate on disk for environment verification.
    /// </summary>
    public string? TlsCertPath { get; set; }

    public string[] FallbackStunServers { get; set; } = new[]
    {
        "stun:stun.l.google.com:19302",
        "stun:stun1.l.google.com:19302"
    };

    /// <summary>
    /// Determines whether TURNS (TLS) can be safely advertised to clients.
    /// </summary>
    public bool IsTlsActive()
    {
        if (!Enabled || !EnableTls || !TlsCertConfigured)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(TlsCertPath) && !File.Exists(TlsCertPath))
        {
            return false;
        }

        return true;
    }
}
