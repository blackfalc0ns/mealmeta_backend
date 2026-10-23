namespace MealMate.Application.Features.UnifiedSupport;

public static class SupportCaseProblemCodes
{
    public const string AuthenticationRequired = "support.authentication_required";
    public const string AccessDenied = "support.access_denied";
    public const string NotFound = "support.case_not_found";
    public const string InvalidRequest = "support.request_invalid";
    public const string InvalidTransition = "support.transition_invalid";
    public const string VersionConflict = "support.version_conflict";
    public const string IdempotencyKeyReused = "support.idempotency_key_reused";
    public const string LinkTargetUnsupported = "support.link_target_unsupported";
    public const string AttachmentUnsupported = "support.attachment_unsupported";
    public const string AttachmentStorageUnavailable = "support.attachment_storage_unavailable";
    public const string AttachmentScanUnavailable = "support.attachment_scan_unavailable";
    public const string AttachmentRejected = "support.attachment_rejected";
    public const string TaskNotFound = "support.task_not_found";
    public const string OperationalRouteRequired = "support.operational_route_required";
}
