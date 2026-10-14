namespace MealMate.Application.Common.Localization;

/// <summary>
/// Stable message codes used across validators, use cases, and domain rules.
/// Resource keys in Messages.resx must match these values exactly.
/// </summary>
public static class MessageCodes
{
    public static class DriverTrip
    {
        public const string Started = "driver.trip.started";
    }

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
        public const string PasswordMismatch = "validation.password.mismatch";
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
        public const string AccountPendingApproval = "auth.account_pending_approval";
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
        public const string RoleMismatch = "auth.role_mismatch";
        public const string RoleMismatchDetail = "auth.role_mismatch_detail";
        public const string NotDeliveryStaff = "auth.not_delivery_staff";
        public const string InvalidStaffRole = "auth.invalid_staff_role";
        public const string OtpResent = "auth.otp_resent";
        public const string OtpVerifiedSetPassword = "auth.otp_verified_set_password";
        public const string ForgotPasswordSent = "auth.forgot_password_sent";
        public const string PasswordResetSuccess = "auth.password_reset_success";
    }

    public static class Driver
    {
        public const string RegistrationSubmitted = "driver.registration_submitted_successfully";
        public const string RestaurantNotFound = "driver.restaurant_not_found";
        public const string RestaurantInactive = "driver.restaurant_inactive";
        public const string PhoneAlreadyRegistered = "auth.phone_already_registered";
        public const string EmailAlreadyRegistered = "auth.email_already_registered";
        public const string NationalIdAlreadyRegistered = "auth.national_id_already_registered";
        public const string VehiclePlateAlreadyRegistered = "auth.vehicle_plate_already_registered";
        public const string ExpiryDateMustBeFuture = "driver.document_expiry_must_be_future";
        public const string FileRequired = "driver.file_required";
        public const string FileUploadSuccess = "driver.file_upload_success";
        public const string RestaurantApprovalRequired = "driver.restaurant_approval_required";
        public const string StatusUnderReviewTitle = "driver.status_under_review_title";
        public const string StatusUnderReviewSubtitle = "driver.status_under_review_subtitle";
        public const string StatusUnderReviewNotice = "driver.status_under_review_notice";
        public const string StatusNeedsChangesTitle = "driver.status_needs_changes_title";
        public const string StatusNeedsChangesSubtitle = "driver.status_needs_changes_subtitle";
        public const string StatusRejectedTitle = "driver.status_rejected_title";
        public const string StatusRejectedSubtitle = "driver.status_rejected_subtitle";
        public const string StatusApprovedTitle = "driver.status_approved_title";
        public const string StatusApprovedSubtitle = "driver.status_approved_subtitle";
        public const string StatusNotFound = "driver.status_not_found";
        public const string ResubmittedSuccessfully = "driver.resubmitted_successfully";

        // Notifications - Restaurant & Admin
        public const string NotificationRestaurantSubmittedTitle = "driver.notification.restaurant_submitted_title";
        public const string NotificationRestaurantSubmittedBody = "driver.notification.restaurant_submitted_body";
        public const string NotificationRestaurantResubmittedTitle = "driver.notification.restaurant_resubmitted_title";
        public const string NotificationRestaurantResubmittedBody = "driver.notification.restaurant_resubmitted_body";
        public const string NotificationAdminReadyTitle = "driver.notification.admin_ready_title";
        public const string NotificationAdminReadyBody = "driver.notification.admin_ready_body";

        // Notifications - Driver
        public const string NotificationDriverSubmittedTitle = "driver.notification.driver_submitted_title";
        public const string NotificationDriverSubmittedBody = "driver.notification.driver_submitted_body";
        public const string NotificationDriverRestaurantApprovedTitle = "driver.notification.driver_restaurant_approved_title";
        public const string NotificationDriverRestaurantApprovedBody = "driver.notification.driver_restaurant_approved_body";
        public const string NotificationDriverRestaurantChangesTitle = "driver.notification.driver_restaurant_changes_title";
        public const string NotificationDriverRestaurantChangesBody = "driver.notification.driver_restaurant_changes_body";
        public const string NotificationDriverRestaurantRejectedTitle = "driver.notification.driver_restaurant_rejected_title";
        public const string NotificationDriverRestaurantRejectedBody = "driver.notification.driver_restaurant_rejected_body";
        public const string NotificationDriverAdminApprovedTitle = "driver.notification.driver_admin_approved_title";
        public const string NotificationDriverAdminApprovedBody = "driver.notification.driver_admin_approved_body";
        public const string NotificationDriverAdminChangesTitle = "driver.notification.driver_admin_changes_title";
        public const string NotificationDriverAdminChangesBody = "driver.notification.driver_admin_changes_body";
        public const string NotificationDriverAdminRejectedTitle = "driver.notification.driver_admin_rejected_title";
        public const string NotificationDriverAdminRejectedBody = "driver.notification.driver_admin_rejected_body";
    }

    public static class DriverSettings
    {
        public const string LanguageInvalid = "driver.settings.language_invalid";
        public const string NavigationAppInvalid = "driver.settings.navigation_app_invalid";
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
        public const string RestaurantCoverageUnavailable = "calendar.restaurant_coverage_unavailable";
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
        public const string CoordinatesPairRequired = "Orders.CoordinatesPairRequired";
        public const string CoordinatesOutOfRange = "Orders.CoordinatesOutOfRange";
        public const string StartDeliveryNotAllowed = "order.start_delivery_not_allowed";
    }

    public static class Tracking
    {
        /// <summary>Ambiguous or inconsistent delivery evidence; no arbitrary box is chosen.</summary>
        public const string Unavailable = "tracking.unavailable";
    }

    public static class DriverIssues
    {
        public static class Report
        {
            public const string StopForbidden = "driver.issue.stop_forbidden";
            public const string IssueTypeInvalid = "driver.issue.issue_type_invalid";
            public const string SeverityInvalid = "driver.issue.severity_invalid";
            public const string DescriptionTooLong = "driver.issue.description_too_long";
            public const string PhotoKeysLimit = "driver.issue.photo_keys_limit";
            public const string PhotoNotFound = "driver.issue.photo_not_found";
            public const string PhotoForbidden = "driver.issue.photo_forbidden";
            public const string PhotoInvalidType = "driver.issue.photo_invalid_type";
        }

        public static class Reassignment
        {
            public const string StopForbidden = "driver.reassignment.stop_forbidden";
            public const string ReasonInvalid = "driver.reassignment.reason_invalid";
            public const string NotesTooLong = "driver.reassignment.notes_too_long";
            public const string RequestAlreadyActive = "driver.reassignment.request_already_active";
            public const string StopStateNotAllowed = "driver.reassignment.stop_state_not_allowed";
        }
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
        public const string BankAccountRequired = "influencer.bank_account_required";
        public const string InvalidIban = "influencer.invalid_iban";
        public const string BankNameRequired = "influencer.bank_name_required";
        public const string AccountHolderRequired = "influencer.account_holder_required";
        public const string CannotCancelWithdrawal = "influencer.cannot_cancel_withdrawal";
        public const string AccountSuspended = "influencer.account_suspended";
        public const string DuplicateWithdrawalRequest = "influencer.duplicate_withdrawal_request";
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
        public const string InvoicePaidReviewRequired = "invoice_paid_review_required";
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

    public static class Dispatcher
    {
        public const string GreetingTitle = "dispatcher.greeting_title";
        public const string GreetingAllUnderControl = "dispatcher.greeting_all_under_control";
        public const string GreetingAttentionNeeded = "dispatcher.greeting_attention_needed";
        public const string RoleLabel = "dispatcher.role_label";
        public const string BadgeTopRated = "dispatcher.badge_top_rated";
        public const string BadgeExcellent = "dispatcher.badge_excellent";
        public const string BadgeVeryGood = "dispatcher.badge_very_good";
        public const string StatusEnRouteToCustomer = "dispatcher.status_en_route_to_customer";
        public const string StatusInDelivery = "dispatcher.status_in_delivery";
        public const string StatusOnBreak = "dispatcher.status_on_break";
        public const string StatusAvailable = "dispatcher.status_available";
        public const string ActiveIssuesSummary = "dispatcher.active_issues_summary";
        public const string LocationUpdated = "dispatcher.location_updated_successfully";
        public const string DriverNotFound = "dispatcher.driver_not_found";
        public const string TrackingNotRequired = "dispatcher.tracking_not_required";
        public const string IssueDeliveryDelayMessage = "dispatcher.issue_delivery_delay_message";
        public const string IssueDriverStationaryMessage = "dispatcher.issue_driver_stationary_message";
    }

    public static class DispatcherOrders
    {
        public const string QueueTitle = "dispatcher_orders.queue_title";
        public const string QueueSubtitle = "dispatcher_orders.queue_subtitle";
        public const string FilterAll = "dispatcher_orders.filter_all";
        public const string FilterPending = "dispatcher_orders.filter_pending";
        public const string FilterAssigned = "dispatcher_orders.filter_assigned";
        public const string FilterInDelivery = "dispatcher_orders.filter_in_delivery";
        public const string FilterIssues = "dispatcher_orders.filter_issues";
        public const string BadgeNew = "dispatcher_orders.badge_new";
        public const string BadgeUrgent = "dispatcher_orders.badge_urgent";
        public const string BadgeHighPriority = "dispatcher_orders.badge_high_priority";
        public const string BadgeNormal = "dispatcher_orders.badge_normal";
        public const string SuggestionNearest = "dispatcher_orders.suggestion_nearest";
        public const string SuggestionLeastLoaded = "dispatcher_orders.suggestion_least_loaded";
        public const string MealsCountFormat = "dispatcher_orders.meals_count_format";
        public const string BoxAssignedSuccess = "dispatcher_orders.box_assigned_success";
        public const string BoxNotFound = "dispatcher_orders.box_not_found";
        public const string DriverStatusAvailable = "dispatcher_orders.driver_status_available";
        public const string DriverStatusBusy = "dispatcher_orders.driver_status_busy";
        public const string DriverStatusInDelivery = "dispatcher_orders.driver_status_in_delivery";
        public const string DriverStatusReturning = "dispatcher_orders.driver_status_returning";
        public const string TagNearest = "dispatcher_orders.tag_nearest";
        public const string TagLeastLoaded = "dispatcher_orders.tag_least_loaded";
        public const string TagBusyDelivering = "dispatcher_orders.tag_busy_delivering";
        public const string TagNearCustomer = "dispatcher_orders.tag_near_customer";
        public const string BoxesCountFormat = "dispatcher_orders.boxes_count_format";
        public const string EstimatedTimeFormat = "dispatcher_orders.estimated_time_format";
        public const string BestSuggestionBadge = "dispatcher_orders.best_suggestion_badge";
        public const string BoxDetailsTitle = "dispatcher_orders.box_details_title";
    }

    public static class DispatcherRoster
    {
        public const string RosterTitle = "dispatcher_roster.roster_title";
        public const string RosterSubtitle = "dispatcher_roster.roster_subtitle";
        public const string TabAllDrivers = "dispatcher_roster.tab_all_drivers";
        public const string TabByArea = "dispatcher_roster.tab_by_area";
        public const string TotalDrivers = "dispatcher_roster.total_drivers";
        public const string AvailableDrivers = "dispatcher_roster.available_drivers";
        public const string BusyNow = "dispatcher_roster.busy_now";
        public const string DriversInAreaFormat = "dispatcher_roster.drivers_in_area_format";
        public const string StatusAvailable = "dispatcher_roster.status_available";
        public const string StatusOnTheWay = "dispatcher_roster.status_on_the_way";
        public const string StatusOnBreak = "dispatcher_roster.status_on_break";
        public const string ButtonSelect = "dispatcher_roster.button_select";
        public const string ButtonUnavailable = "dispatcher_roster.button_unavailable";
        public const string ActiveOrders = "dispatcher_roster.active_orders";
        public const string CompletedOrdersToday = "dispatcher_roster.completed_orders_today";
        public const string DistanceFromYou = "dispatcher_roster.distance_from_you";
        public const string ViewDriversOnMap = "dispatcher_roster.view_drivers_on_map";
    }

    public static class DispatcherLiveMap
    {
        public const string MapTitle = "dispatcher_live_map.map_title";
        public const string MapSubtitle = "dispatcher_live_map.map_subtitle";
        public const string KpiActiveDrivers = "dispatcher_live_map.kpi_active_drivers";
        public const string KpiInDelivery = "dispatcher_live_map.kpi_in_delivery";
        public const string KpiPaused = "dispatcher_live_map.kpi_paused";
        public const string KpiAttentionRequired = "dispatcher_live_map.kpi_attention_required";
        public const string StatusInDelivery = "dispatcher_live_map.status_in_delivery";
        public const string StatusLoading = "dispatcher_live_map.status_loading";
        public const string StatusPaused = "dispatcher_live_map.status_paused";
        public const string StatusAttention = "dispatcher_live_map.status_attention";
        public const string DistanceRemainingFormat = "dispatcher_live_map.distance_remaining_format";
        public const string DistanceUnavailable = "dispatcher_live_map.distance_unavailable";
        public const string LocationLabel = "dispatcher_live_map.location_label";
        public const string RemainingDistanceLabel = "dispatcher_live_map.remaining_distance_label";
    }

    public static class DispatcherDriverDetails
    {
        public const string ScreenTitle = "dispatcher_driver_details.screen_title";
        public const string LastUpdatedNow = "dispatcher_driver_details.last_updated_now";
        public const string LastUpdatedMinutesAgo = "dispatcher_driver_details.last_updated_minutes_ago";
        public const string RatingLabel = "dispatcher_driver_details.rating_label";
        public const string AvgDelayLabel = "dispatcher_driver_details.avg_delay_label";
        public const string CompletedTodayLabel = "dispatcher_driver_details.completed_today_label";
        public const string CurrentBoxesLabel = "dispatcher_driver_details.current_boxes_label";
        public const string CurrentLocationTitle = "dispatcher_driver_details.current_location_title";
        public const string StatusOutForDelivery = "dispatcher_driver_details.status_out_for_delivery";
        public const string StatusReceived = "dispatcher_driver_details.status_received";
        public const string ActiveBoxesSection = "dispatcher_driver_details.active_boxes_section";
        public const string DailySummaryTitle = "dispatcher_driver_details.daily_summary_title";
        public const string ApproxDistanceLabel = "dispatcher_driver_details.approx_distance_label";
        public const string FailedDeliveryLabel = "dispatcher_driver_details.failed_delivery_label";
        public const string CustomerPrefix = "dispatcher_driver_details.customer_prefix";
        public const string ScheduledPrefix = "dispatcher_driver_details.scheduled_prefix";
        public const string CallButton = "dispatcher_driver_details.call_button";
        public const string SendMessageButton = "dispatcher_driver_details.send_message_button";
    }

    public static class DispatcherBoxTracking
    {
        public const string ScreenTitle = "dispatcher_box_tracking.screen_title";
        public const string StatusOnTheWay = "dispatcher_box_tracking.status_on_the_way";
        public const string StepReadyTitle = "dispatcher_box_tracking.step_ready_title";
        public const string StepReadyDescription = "dispatcher_box_tracking.step_ready_description";
        public const string StepPickedUpTitle = "dispatcher_box_tracking.step_picked_up_title";
        public const string StepPickedUpDescription = "dispatcher_box_tracking.step_picked_up_description";
        public const string StepOnTheWayTitle = "dispatcher_box_tracking.step_on_the_way_title";
        public const string StepOnTheWayDescription = "dispatcher_box_tracking.step_on_the_way_description";
        public const string StepDeliveredTitle = "dispatcher_box_tracking.step_delivered_title";
        public const string StepDeliveredDescription = "dispatcher_box_tracking.step_delivered_description";
        public const string IssueReportedSuccess = "dispatcher_box_tracking.issue_reported_success";
    }

    public static class DispatcherPerformance
    {
        public const string ScreenTitle = "dispatcher_performance.screen_title";
        public const string TabOverview = "dispatcher_performance.tab_overview";
        public const string TabComparison = "dispatcher_performance.tab_comparison";
        public const string PeriodLast7Days = "dispatcher_performance.period_last_7_days";
        public const string PeriodToday = "dispatcher_performance.period_today";
        public const string PeriodThisWeek = "dispatcher_performance.period_this_week";
        public const string PeriodThisMonth = "dispatcher_performance.period_this_month";
        public const string KpiTotalBoxes = "dispatcher_performance.kpi_total_boxes";
        public const string KpiDelivered = "dispatcher_performance.kpi_delivered";
        public const string KpiAvgDelay = "dispatcher_performance.kpi_avg_delay";
        public const string KpiOverallRating = "dispatcher_performance.kpi_overall_rating";
        public const string KpiFailedDelivery = "dispatcher_performance.kpi_failed_delivery";
        public const string SegmentOnTime = "dispatcher_performance.segment_on_time";
        public const string SegmentDelayed = "dispatcher_performance.segment_delayed";
        public const string SegmentFailed = "dispatcher_performance.segment_failed";
        public const string SegmentCancelled = "dispatcher_performance.segment_cancelled";
        public const string TopDriversTitle = "dispatcher_performance.top_drivers_title";
        public const string DriversTableTitle = "dispatcher_performance.drivers_table_title";
    }

    public static class DispatcherSupport
    {
        public const string ScreenTitle = "dispatcher_support.screen_title";
        public const string ScreenSubtitle = "dispatcher_support.screen_subtitle";
        public const string ProblemDetailsTitle = "dispatcher_support.problem_details_title";
        public const string AssignReplacementTitle = "dispatcher_support.assign_replacement_title";
        public const string StatusResolved = "dispatcher_support.status_resolved";
        public const string StatusInResolution = "dispatcher_support.status_in_resolution";
        public const string StatusOpen = "dispatcher_support.status_open";
        public const string PriorityHigh = "dispatcher_support.priority_high";
        public const string PriorityMedium = "dispatcher_support.priority_medium";
        public const string PriorityLow = "dispatcher_support.priority_low";
        public const string IssueNotFound = "dispatcher_support.issue_not_found";
        public const string ReassignSuccess = "dispatcher_support.reassign_success";
        public const string ResolveSuccess = "dispatcher_support.resolve_success";
        public const string ReplacementCandidatesSubtitle = "dispatcher_support.replacement_candidates_subtitle";
    }

    public static class VoiceCalls
    {
        /// <summary>Localized participant role shown on the customer call screen.</summary>
        public const string CallScreenDriverRole = "voice.call_screen.driver_role";
    }

    public static class Campaign
    {
        public const string ReviewWindowMissing = "campaign.review_window_missing";
        public const string ReviewWindowNotStarted = "campaign.review_window_not_started";
        public const string ReviewWindowExpired = "campaign.review_window_expired";
        public const string ReviewWindowNotOpen = "campaign.review_window_not_open";
        public const string ReviewWindowOrderingInvalid = "campaign.review_window_ordering_invalid";
        public const string ReviewWindowIncomplete = "campaign.review_window_incomplete";
        public const string EnrollmentDatesRequired = "campaign.enrollment_dates_required";
        public const string ApprovalStale = "campaign.approval_stale";
        public const string AlreadyResponded = "campaign.already_responded";
        public const string EnrollmentOwnershipMismatch = "campaign.enrollment_ownership_mismatch";
        public const string PricingPolicyNotApproved = "campaign.pricing_policy_not_approved";
        public const string PricingPolicyCurrencyMismatch = "campaign.pricing_policy_currency_mismatch";
        public const string ConcurrentModification = "campaign.concurrent_modification";
    }
}
