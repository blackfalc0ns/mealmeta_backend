using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MealMate.Infrastructure.Push;

public sealed class FirebasePushOptions
{
    public const string SectionName = "Firebase";

    public string? ProjectId { get; set; }
    public string? CredentialPath { get; set; }
}

public sealed class FirebaseFcmMessagingClient : IFcmMessagingClient, IDisposable
{
    private readonly FirebasePushOptions _options;
    private readonly ILogger<FirebaseFcmMessagingClient> _logger;
    private FirebaseApp? _app;
    private readonly object _initLock = new();

    public FirebaseFcmMessagingClient(IOptions<FirebasePushOptions> options, ILogger<FirebaseFcmMessagingClient> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => TryEnsureInitialized();

    public async Task<FcmSendResult> SendAsync(
        string deviceToken,
        string title,
        string body,
        IReadOnlyDictionary<string, string> data,
        CancellationToken cancellationToken = default)
    {
        if (!TryEnsureInitialized())
            return FcmSendResult.Failed("FCM is not configured.");

        try
        {
            var message = new Message
            {
                Token = deviceToken,
                Notification = new Notification
                {
                    Title = title,
                    Body = body,
                },
                Data = data.ToDictionary(x => x.Key, x => x.Value),
                Android = new AndroidConfig { Priority = Priority.High },
                Apns = new ApnsConfig
                {
                    Aps = new Aps
                    {
                        Sound = "default",
                    },
                },
            };

            _ = await FirebaseMessaging.GetMessaging(_app).SendAsync(message, cancellationToken);
            return FcmSendResult.Success();
        }
        catch (FirebaseMessagingException ex) when (IsInvalidToken(ex))
        {
            return FcmSendResult.InvalidToken(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FCM send failed.");
            return FcmSendResult.Failed(ex.Message);
        }
    }

    public void Dispose()
    {
        if (_app is not null)
            _app.Delete();
    }

    private bool TryEnsureInitialized()
    {
        if (_app is not null)
            return true;

        lock (_initLock)
        {
            if (_app is not null)
                return true;

            var credentialPath = ResolveCredentialPath();
            if (string.IsNullOrWhiteSpace(credentialPath) || !File.Exists(credentialPath))
            {
                _logger.LogWarning("Firebase push is disabled: credential file was not found.");
                return false;
            }

            var projectId = _options.ProjectId ?? Environment.GetEnvironmentVariable("FIREBASE_PROJECT_ID");
            if (string.IsNullOrWhiteSpace(projectId))
            {
                _logger.LogWarning("Firebase push is disabled: ProjectId is missing.");
                return false;
            }

            _app = FirebaseApp.Create(new AppOptions
            {
                Credential = GoogleCredential.FromFile(credentialPath),
                ProjectId = projectId,
            }, $"MealMate-{projectId}");

            _logger.LogInformation("Firebase Cloud Messaging initialized for project {ProjectId}.", projectId);
            return true;
        }
    }

    private string? ResolveCredentialPath()
    {
        if (!string.IsNullOrWhiteSpace(_options.CredentialPath))
            return _options.CredentialPath;

        return Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS");
    }

    private static bool IsInvalidToken(FirebaseMessagingException ex)
        => ex.MessagingErrorCode is MessagingErrorCode.Unregistered
            or MessagingErrorCode.InvalidArgument
            or MessagingErrorCode.SenderIdMismatch;
}
