using System.Text.Json;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using MealMate.Application.Features.VoiceCalls.Abstractions;
using MealMate.Application.Features.VoiceCalls.V2.Dtos;
using MealMate.Infrastructure.Push;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace MealMate.Infrastructure.VoiceCalls;

/// <summary>Call-specific data-only transport. Provider acceptance is not device acknowledgement.</summary>
public sealed class FirebaseVoiceCallV2Provider(IOptions<FirebasePushOptions> options, IHostEnvironment environment)
    : IVoiceCallV2FcmProvider, IDisposable
{
    private FirebaseApp? _app;
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static Message BuildMessage(string token, CallEventEnvelope<CallSnapshotDto> envelope, TimeSpan ttl) => new()
    {
        Token = token,
        Data = new Dictionary<string, string>
        {
            ["type"] = envelope.EventType,
            ["envelope"] = JsonSerializer.Serialize(envelope, Json)
        },
        Android = new AndroidConfig { Priority = Priority.High, TimeToLive = ttl },
        Apns = new ApnsConfig
        {
            Headers = new Dictionary<string, string>
            {
                ["apns-push-type"] = "background", ["apns-priority"] = "5", ["apns-expiration"] = "0"
            },
            Aps = new Aps { ContentAvailable = true }
        }
    };

    public async Task<VoiceCallPushResult> SendAsync(string token, CallEventEnvelope<CallSnapshotDto> envelope,
        TimeSpan ttl, CancellationToken cancellationToken)
    {
        EnsureInitialized();
        try
        {
            var id = await FirebaseMessaging.GetMessaging(_app).SendAsync(BuildMessage(token, envelope, ttl), cancellationToken);
            return new(VoiceCallPushOutcome.Accepted, id);
        }
        catch (FirebaseMessagingException error) when (error.MessagingErrorCode == MessagingErrorCode.Unregistered)
        {
            return new(VoiceCallPushOutcome.InvalidRoute);
        }
        // Credential, quota, availability and payload errors remain visible to the retrying outbox.
    }

    private void EnsureInitialized()
    {
        lock (_gate)
        {
            if (_app is not null) return;
            var path = FirebaseCredentialPathResolver.Resolve(options.Value.CredentialPath, environment.ContentRootPath,
                Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS"));
            var project = options.Value.ProjectId ?? Environment.GetEnvironmentVariable("FIREBASE_PROJECT_ID");
            if (path is null || string.IsNullOrWhiteSpace(project))
                throw new InvalidOperationException("Voice-call FCM credentials and project are required for an existing push route.");
            _app = FirebaseApp.Create(new AppOptions { Credential = GoogleCredential.FromFile(path), ProjectId = project }, "MealMate-VoiceCallsV2-" + project);
        }
    }
    public void Dispose() { lock (_gate) { _app?.Delete(); _app = null; } }
}
