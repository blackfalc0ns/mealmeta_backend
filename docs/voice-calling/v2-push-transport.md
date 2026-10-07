# Voice-call v2 push transport

The persisted v2 outbox owns event identity and recipient enumeration. `DeviceVoiceCallV2PushTransport` sends only the recipient device session's owned `FcmDeviceTokenId`; it never enumerates all account tokens. A missing, expired, revoked, unsupported or foreign-owned route does not become provider acceptance. Confirmed invalid routes deactivate their exact persisted registry row. Provider availability, credential, quota and payload failures propagate for outbox retries.

Android receives FCM **data only**, high priority, containing the complete camelCase event envelope in `data.envelope` and the semantic event type in `data.type`. No provider alert title/body is attached. The incoming TTL is the smaller of the authoritative ring deadline and envelope expiration, minus server time; an expired/missing deadline suppresses incoming delivery. Terminal messages use the event expiration, or immediate delivery without storage when absent. Event IDs, sequences, protocol versions and snapshots are preserved across retries. Clients must localize semantic call state using their active language and must deduplicate by event ID, apply monotonic sequences and refuse expired ringing UI.

An iOS incoming event uses the registered **APNs VoIP token**, direct HTTP/2 with exact version negotiation, ES256 provider authentication, `apns-push-type: voip`, priority 10, `<recipient-app-bundle>.voip`, the persisted event UUID as `apns-id`, and the ring expiration. Terminal/answered events use the same device's FCM token with a background APNs configuration. Connecting, connected and ringing updates use realtime only. Provider acceptance is never a device acknowledgement.

Configure existing `Firebase:ProjectId` and `Firebase:CredentialPath` (or the supported Firebase environment variables). Configure `VoiceCalls:Apns:TeamId`, `KeyId`, `BundleId` for the customer app and `DriverBundleId` for the driver app, plus `PrivateKeyPath` or secret-backed `PrivateKeyPem`. `UseSandbox` selects Apple's sandbox; its default is production. Store signing credentials in the deployment secret system, never source control. The recipient application bundle is selected from the snapshot's authoritative local participant role. A route with missing credentials throws and remains retryable.

Legacy v1 visible notification copy is embedded in English and Arabic resource catalogs. It follows persisted customer `PreferredLanguage`, or the established driver settings language; an unset locale follows the customer Arabic default. V2 contains semantic data only so native call UI can remain reactive to the client's active language.

## Real-device release gate

Automated tests capture outgoing APNs HTTP requests, verify signing, provider error classification, stable envelope fields, data-only FCM settings, route ownership and registry invalidation. They cannot prove operating-system wakeup, native CallKit/Android call UI or audio. Before releasing mobile calling, use actual customer and driver app credentials and physical devices to verify:

- Android foreground, background and terminated states: incoming high-priority delivery, deadline enforcement, cancellation/answered-elsewhere cleanup, and audible bidirectional media.
- iOS PushKit/CallKit foreground, background and terminated states: incoming APNs VoIP wakeup and native call report, correct app topic/environment, and expired-event handling.
- Terminal and answered-elsewhere cleanup on every recipient installation, including iOS background delivery restrictions; reconnect/snapshot recovery must remove stale native UI when background delivery is delayed.
- Two devices per account, token rotation/revocation and invalid-token responses. Only the selected device route should receive each outbox delivery.
- Arabic and English native call text, active-language switching, RTL/LTR and accessibility labels in the actual client.

Primary provider references: [Apple APNs requests](https://developer.apple.com/documentation/usernotifications/sending-notification-requests-to-apns), [Apple APNs connection](https://developer.apple.com/documentation/usernotifications/establishing-a-connection-to-apns), [Firebase Android priority](https://firebase.google.com/docs/cloud-messaging/android-message-priority), [Firebase .NET AndroidConfig](https://firebase.google.com/docs/reference/admin/dotnet/class/firebase-admin/messaging/android-config).
