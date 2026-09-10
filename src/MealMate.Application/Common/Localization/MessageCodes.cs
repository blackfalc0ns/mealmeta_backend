namespace MealMate.Application.Common.Localization;

/// <summary>
/// Stable message codes used across validators, use cases, and domain rules.
/// Resource keys in Messages.resx must match these values exactly.
/// </summary>
public static class MessageCodes
{
    public static class Titles
    {
        public const string ValidationFailed = "errors.validation.title";
        public const string DomainViolation = "errors.domain.title";
        public const string Unauthorized = "errors.unauthorized.title";
        public const string ServerError = "errors.server.title";
    }

    public static class Common
    {
        public const string ServerError = "errors.common.server_error";
        public const string Unauthorized = "errors.common.unauthorized";
        public const string ValidationFailed = "errors.validation.failed";
        public const string Unknown = "errors.common.unknown";
        public const string NotFound = "errors.common.not_found";
    }

    public static class Validation
    {
        public const string Unknown = "validation.unknown";
        public const string Required = "validation.required";
        public const string RequiredField = "validation.required_field";
        public const string EmailInvalid = "validation.email.invalid";
        public const string InvalidFormat = "validation.invalid_format";
        public const string PasswordMinimumLength = "validation.password.minimum_length_8";
        public const string OtpCodeInvalid = "validation.otp_code.invalid";
        public const string MaxLength = "validation.max_length";
        public const string MinLength = "validation.min_length";
        public const string GreaterThan = "validation.greater_than";
        public const string LessThan = "validation.less_than";
        public const string InclusiveBetween = "validation.inclusive_between";
        public const string DuplicateCode = "validation.duplicate_code";
        public const string InvalidStateTransition = "validation.invalid_state_transition";
    }

    public static class Domain
    {
        public const string Generic = "domain.generic";
        public const string MoneyCurrencyRequired = "domain.money.currency_required";
        public const string MoneyCurrencyMismatch = "domain.money.currency_mismatch";
        public const string PercentageNegative = "domain.percentage.negative";
    }

    public static class Auth
    {
        public const string InvalidCredentials = "auth.invalid_credentials";
        public const string AccountSuspended = "auth.account_suspended";
        public const string AccountClosed = "auth.account_closed";
        public const string DuplicateEmail = "auth.duplicate_email";
        public const string DuplicatePhone = "auth.duplicate_phone";
        public const string InvalidRefreshToken = "auth.invalid_refresh_token";
        public const string OtpInvalid = "auth.otp_invalid";
        public const string OtpExpired = "auth.otp_expired";
        public const string OtpTooManyAttempts = "auth.otp_too_many_attempts";
        public const string PasswordTooWeak = "auth.password_too_weak";
        public const string Forbidden = "auth.forbidden";
        public const string SelfPrivilegeEscalation = "auth.self_privilege_escalation";
        public const string UserNotFound = "auth.user_not_found";
        public const string RoleNotFound = "auth.role_not_found";
        public const string InvalidState = "auth.invalid_state";
        public const string DocumentsIncomplete = "auth.documents_incomplete";
        public const string RestaurantRegistrationExists = "auth.restaurant_registration_exists";
        public const string DuplicateCommercialRegistration = "auth.duplicate_commercial_registration";
        public const string DuplicateInfluencerCode = "auth.duplicate_influencer_code";
        public const string InvitationInvalid = "auth.invitation_invalid";
        public const string AccountLocked = "auth.account_locked";
        public const string OtpSendLimitExceeded = "auth.otp_send_limit_exceeded";
        public const string TotpInvalid = "auth.totp_invalid";
        public const string TotpChallengeInvalid = "auth.totp_challenge_invalid";
        public const string TotpChallengeExpired = "auth.totp_challenge_expired";
        public const string TotpChallengeTooManyAttempts = "auth.totp_challenge_too_many_attempts";
        public const string TotpSetupNotAllowed = "auth.totp_setup_not_allowed";
        public const string TotpRequired = "auth.totp_required";
        public const string TotpEnrollmentFailed = "auth.totp_enrollment_failed";
    }

    public static class RestaurantOperations
    {
        public const string CapacityCannotReduce = "capacity.cannot_reduce";
        public const string ConfirmedMealsExist = "working_days.confirmed_meals_exist";
        public const string RegionNotFound = "region.not_found";
        public const string InvalidDeliveryWindow = "service_region.invalid_window";
    }

    public static class RestaurantMeals
    {
        public const string IngredientNotFound = "errors.ingredient.not_found";
        public const string IngredientInvalidAllergen = "ingredient.invalid_allergen";
        public const string IngredientInvalidDietaryPreference = "ingredient.invalid_dietary_preference";
        public const string MealNotFound = "meal.not_found";
        public const string MealAlreadyApproved = "meal.already_approved";
        public const string MealBundleProgramMismatch = "meal.bundle_program_mismatch";
        public const string MealCannotWithdraw = "meal.cannot_withdraw";
        public const string MealIngredientNotFound = "meal.ingredient_not_found";
        public const string MealIngredientPortionInvalid = "meal.ingredient_portion_invalid";
        public const string MealIngredientRestaurantMismatch = "meal.ingredient_restaurant_mismatch";
        public const string MealInvalidAllergen = "meal.invalid_allergen";
        public const string MealInvalidDietaryPreference = "meal.invalid_dietary_preference";
        public const string MealLockedCycleAssignment = "meal.locked_cycle_assignment";
        public const string MealMenuDaysInvalid = "meal.menu_days_invalid";
        public const string MealNotApproved = "meal.not_approved";
        public const string MealNotReadyForApproval = "meal.not_ready_for_approval";
        public const string MealNutritionOutOfRange = "meal.nutrition_out_of_range";
        public const string MealSlotInvalid = "meal.slot_invalid";
        public const string MealProgramRequired = "meal.program_required";
        public const string MealProgramNotSelected = "meal.program_not_selected";
        public const string MealBundleNotSelected = "meal.bundle_not_selected";
    }

    public static class MenuCycles
    {
        public const string DayNotFound = "menu_cycle.day_not_found";
        public const string DaysInvalid = "menu_cycle.days_invalid";
        public const string EffectiveDateConflict = "menu_cycle.effective_date_conflict";
        public const string FutureDateRequired = "menu_cycle.future_date_required";
        public const string MealLinkNotFound = "menu_cycle.meal_link_not_found";
        public const string NoActiveCycle = "menu_cycle.no_active_cycle";
        public const string NoApprovedVersion = "menu_cycle.no_approved_version";
        public const string NotEditable = "menu_cycle.not_editable";
        public const string NotFound = "menu_cycle.not_found";
        public const string NotReady = "menu_cycle.not_ready";
        public const string NotUnderReview = "menu_cycle.not_under_review";
        public const string Paused = "menu_cycle.paused";
        public const string PreviewRange = "menu_cycle.preview_range";
        public const string ReviewDecisionInvalid = "menu_cycle.review_decision_invalid";
        public const string SlotsInvalid = "menu_cycle.slots_invalid";
    }

    public static class MenuPlans
    {
        public const string InvalidProgram = "menu_plan.invalid_program";
        public const string ModeMismatch = "menu_plan.mode_mismatch";
        public const string NotFoundForDate = "menu_plan.not_found_for_date";
        public const string ProgramNotSelected = "menu_plan.program_not_selected";
        public const string ProgramOverlap = "menu_plan.program_overlap";
        public const string ProgramRequired = "menu_plan.program_required";
        public const string VersionNotFound = "menu_plan.version_not_found";
        public const string WeekdayLinkNotFound = "menu_plan.weekday_link_not_found";
        public const string ProgramNotFound = "program.not_found";
    }

    public static class Calendar
    {
        public const string FulfillmentNotReady = "calendar.fulfillment_not_ready";
        public const string DayUnavailable = "calendar.day_unavailable";
        public const string SelectionLocked = "calendar.selection_locked";
        public const string RestaurantNotInPool = "calendar.restaurant_not_in_pool";
        public const string QuotaExhausted = "calendar.quota_exhausted";
        public const string RestaurantBusy = "calendar.restaurant_busy";
        public const string InvalidOrRestrictedMeal = "calendar.invalid_or_restricted_meal";
        public const string SlotMismatch = "calendar.slot_mismatch";
        public const string BoxIncomplete = "calendar.box_incomplete";
        public const string NoSafeBox = "calendar.no_safe_box";
        public const string IndividualOnly = "calendar.individual_only";
        public const string BoxNotReady = "calendar.box_not_ready";
        public const string AllergyConflict = "calendar.allergy_conflict";
        public const string AllergyUncovered = "calendar.allergy_uncovered";
    }

    public static class Orders
    {
        public const string NotFound = "order.not_found";
        public const string InvalidStatus = "order.invalid_status";
    }

    public static class Family
    {
        public const string GroupNotActive = "family.group_not_active";
        public const string MemberNotFound = "family.member_not_found";
        public const string MemberPaused = "family.member_paused";
        public const string PermissionDenied = "family.permission_denied";
        public const string CannotRemoveManager = "family.cannot_remove_manager";
        public const string ManagerPermissionsImmutable = "family.manager_permissions_immutable";
        public const string MemberRequiresPayment = "family.member_requires_payment";
        public const string UseCheckoutFlow = "family.use_checkout_flow";
        public const string InviteConflict = "family.invite_conflict";
        public const string InviteInvalid = "family.invite_invalid";
        public const string NotManager = "family.not_manager";
        public const string SessionExpired = "family.session_expired";
    }

    public static class Maps
    {
        public const string ApiKeyMissing = "maps.api_key_missing";
        public const string GeocodeFailed = "maps.geocode_failed";
        public const string GeocodeUnresolved = "maps.geocode_unresolved";
    }

    public static class Influencers
    {
        public const string ProfileNotFound = "influencer.profile_not_found";
        public const string ActiveWithdrawalExists = "influencer.active_withdrawal_exists";
        public const string InsufficientBalance = "influencer.insufficient_available_balance";
        public const string WithdrawalBelowMinimum = "influencer.withdrawal_below_minimum";
        public const string WithdrawalNotFound = "influencer.withdrawal_not_found";
    }

    public static class Subscriptions
    {
        public const string NotFound = "subscription.not_found";
        public const string RenewalNotAvailable = "subscription.renewal_not_available";
        public const string UpgradeNotAvailable = "subscription.upgrade_not_available";
        public const string UpgradeTargetInvalid = "subscription.upgrade_target_invalid";
        public const string UpgradeRequestPending = "subscription.upgrade_request_pending";
        public const string TargetTierUnavailable = "subscription.target_tier_unavailable";
        public const string UpgradeRequestAlreadyPending = "subscription.upgrade_request_already_pending";
        public const string RedirectUrlsRequired = "upgrade.redirect_urls_required";
        public const string CustomerNotFound = "upgrade.customer_not_found";
        public const string InvoiceNotFound = "invoice.not_found";
        public const string SavedCardNotAvailable = "saved_card_not_available";
        public const string NoCurrentSubscription = "no_current_subscription";
        public const string InvoiceDocumentNotAvailable = "invoice_document_not_available";
    }

    public static class Payment
    {
        public const string WebhookSecretNotConfigured = "payment.webhook_secret_not_configured";
        public const string WebhookPayloadInvalid = "payment.webhook_v2_payload_invalid";
        public const string WebhookEventIgnored = "payment.webhook_event_ignored";
        public const string InvalidWebhookSignature = "payment.invalid_webhook_signature";
        public const string CustomerReferenceInvalid = "payment.customer_reference_invalid";
        public const string WebhookAmountInvalid = "payment.webhook_amount_invalid";
        public const string GatewayVerificationUnavailable = "payment.gateway_verification_unavailable";
        public const string WebhookProcessedSuccessfully = "payment.webhook_processed_successfully";
    }
}
