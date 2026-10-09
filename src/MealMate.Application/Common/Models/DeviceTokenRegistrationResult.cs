namespace MealMate.Application.Common.Models;

/// <summary>
/// Persistent registry identifier of an active FCM device-token row. Clients
/// pass it as <c>fcmDeviceTokenId</c> when registering a VoiceCalls v2 device
/// session; re-registering the same token returns the same id.
/// </summary>
public sealed record DeviceTokenRegistrationResult(Guid FcmDeviceTokenId);
