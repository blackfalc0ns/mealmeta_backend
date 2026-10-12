# المسارات والـrequest والـresponse

قاعدة المسارات الثلاث: family-subscription-flow للتجهيز والدفع، family للإدارة، subscriptions للعمليات والتقويم. كل المسارات هنا authenticated. JSON الخاص بالأنواع موجود في الملفات 02–06؛ أسماء DTOs تربط المسار بشكله. GET بلا body. لا تغلف success في data/result: HandleResult<T> يعيد DTO مباشرة.

## GET /api/v1/customer/family-subscription-flow/metadata

Body: لا يوجد body معلن.

Response المعلن: `FamilyDraftMetadataDto` — Status200OK.

## PUT /api/v1/customer/family-subscription-flow/members/{id:guid}/checkout-selection

Body: `SetFamilyCheckoutSelectionRequest` (JSON في مرجع النوع).

Response المعلن: `FamilyCheckoutSelectionDto` — Status200OK.

## GET /api/v1/customer/family-subscription-flow/relationships

Body: لا يوجد body معلن.

الاستجابة: راجع شرح العملية؛ بعض العمليات ترجع anonymous JSON أو قائمة.

## GET /api/v1/customer/family-subscription-flow/programs

Body: لا يوجد body معلن.

Query: `page` (int?, default null), `pageSize` (int?, default null).

الاستجابة: راجع شرح العملية؛ بعض العمليات ترجع anonymous JSON أو قائمة.

## GET /api/v1/customer/family-subscription-flow/programs/{programId:guid}/bundles

Body: لا يوجد body معلن.

الاستجابة: راجع شرح العملية؛ بعض العمليات ترجع anonymous JSON أو قائمة.

## GET /api/v1/customer/family-subscription-flow/programs/{programId:guid}/durations

Body: لا يوجد body معلن.

Query: `includeFriday` (bool, default حسب المصدر).

الاستجابة: راجع شرح العملية؛ بعض العمليات ترجع anonymous JSON أو قائمة.

## GET /api/v1/customer/family-subscription-flow/programs/{programId:guid}/bundles/{bundleId:guid}/durations

Body: لا يوجد body معلن.

Query: `includeFriday` (bool, default حسب المصدر).

الاستجابة: راجع شرح العملية؛ بعض العمليات ترجع anonymous JSON أو قائمة.

## GET /api/v1/customer/family-subscription-flow/programs/{programId:guid}/tiers

Body: لا يوجد body معلن.

Query: `durationDays` (int?, default حسب المصدر), `includeFriday` (bool, default حسب المصدر), `familyMemberId` (Guid?, default حسب المصدر).

الاستجابة: راجع شرح العملية؛ بعض العمليات ترجع anonymous JSON أو قائمة.

## GET /api/v1/customer/family-subscription-flow/programs/{programId:guid}/bundles/{bundleId:guid}/tiers

Body: لا يوجد body معلن.

Query: `durationDays` (int?, default حسب المصدر), `includeFriday` (bool, default حسب المصدر), `familyMemberId` (Guid?, default حسب المصدر).

الاستجابة: راجع شرح العملية؛ بعض العمليات ترجع anonymous JSON أو قائمة.

## GET /api/v1/customer/family-subscription-flow/current

Body: لا يوجد body معلن.

Response المعلن: `FamilyGroupScreenDto` — Status200OK.

## PUT /api/v1/customer/family-subscription-flow/group

Body: `UpsertFamilyGroupRequest` (JSON في مرجع النوع).

Response المعلن: `FamilyGroupScreenDto` — Status200OK.

## POST /api/v1/customer/family-subscription-flow/convert-from-individual

Body: `ConvertFromIndividualRequest?` (JSON في مرجع النوع).

Response المعلن: `FamilyGroupScreenDto` — Status200OK.

## POST /api/v1/customer/family-subscription-flow/abandon-draft

Body: لا يوجد body معلن.

الاستجابة: راجع شرح العملية؛ بعض العمليات ترجع anonymous JSON أو قائمة.

## GET /api/v1/customer/family-subscription-flow/packages

Body: لا يوجد body معلن.

الاستجابة: راجع شرح العملية؛ بعض العمليات ترجع anonymous JSON أو قائمة.

## POST /api/v1/customer/family-subscription-flow/members/lookup

Body: `FamilyMemberLookupRequest` (JSON في مرجع النوع).

Response المعلن: `FamilyMemberLookupDto` — Status200OK.

## PUT /api/v1/customer/family-subscription-flow/members/{memberId:guid}/allergies

Body: `SaveFamilyMemberAllergiesRequest` (JSON في مرجع النوع).

الاستجابة: راجع شرح العملية؛ بعض العمليات ترجع anonymous JSON أو قائمة.

## GET /api/v1/customer/family-subscription-flow/members/{memberId:guid}/allergies

Body: لا يوجد body معلن.

Response المعلن: `FamilyMemberAllergiesDto` — Status200OK.

## PUT /api/v1/customer/family-subscription-flow/members/{memberId:guid}/disliked-ingredients

Body: `SaveFamilyMemberDislikedFoodsRequest` (JSON في مرجع النوع).

الاستجابة: راجع شرح العملية؛ بعض العمليات ترجع anonymous JSON أو قائمة.

## GET /api/v1/customer/family-subscription-flow/members/{memberId:guid}/disliked-ingredients

Body: لا يوجد body معلن.

Response المعلن: `FamilyMemberDislikedFoodsDto` — Status200OK.

## PUT /api/v1/customer/family-subscription-flow/members/{memberId:guid}/dietary

Body: `SaveFamilyMemberDietaryRequest` (JSON في مرجع النوع).

الاستجابة: راجع شرح العملية؛ بعض العمليات ترجع anonymous JSON أو قائمة.

## GET /api/v1/customer/family-subscription-flow/members/{memberId:guid}/dietary

Body: لا يوجد body معلن.

Response المعلن: `FamilyMemberDietaryDto` — Status200OK.

## GET /api/v1/customer/family-subscription-flow/allergies

Body: لا يوجد body معلن.

Query: `page` (int, default 1), `pageSize` (int, default 20), `search` (string?, default null).

Response المعلن: `CustomerAllergiesScreenDto` — Status200OK.

## GET /api/v1/customer/family-subscription-flow/disliked-ingredients

Body: لا يوجد body معلن.

Query: `page` (int, default 1), `pageSize` (int, default 20), `search` (string?, default null).

Response المعلن: `CustomerDislikedIngredientsScreenDto` — Status200OK.

## GET /api/v1/customer/family-subscription-flow/dietary

Body: لا يوجد body معلن.

Query: `page` (int, default 1), `pageSize` (int, default 20), `search` (string?, default null).

Response المعلن: `CustomerDietaryPreferencesScreenDto` — Status200OK.

## GET /api/v1/customer/family-subscription-flow/food-preferences/catalog

Body: لا يوجد body معلن.

Response المعلن: `CustomerFoodPreferenceCatalogDto` — Status200OK.

## GET /api/v1/customer/family-subscription-flow/members/{memberId:guid}/addresses

Body: لا يوجد body معلن.

الاستجابة: راجع شرح العملية؛ بعض العمليات ترجع anonymous JSON أو قائمة.

## POST /api/v1/customer/family-subscription-flow/members/{memberId:guid}/addresses

Body: `CustomerAddressRequest` (JSON في مرجع النوع).

Response المعلن: `CustomerAddressDto` — Status200OK.

## PUT /api/v1/customer/family-subscription-flow/members/{memberId:guid}/addresses/{addressId:guid}

Body: `CustomerAddressRequest` (JSON في مرجع النوع).

Response المعلن: `CustomerAddressDto` — Status200OK.

## POST /api/v1/customer/family-subscription-flow/members/{memberId:guid}/addresses/preview-coverage

Body: `PreviewAddressCoverageRequest` (JSON في مرجع النوع).

Response المعلن: `AddressCoveragePreviewDto` — Status200OK.

## GET /api/v1/customer/family-subscription-flow/members/{memberId:guid}/addresses/{addressId:guid}/preview-coverage

Body: لا يوجد body معلن.

Query: `page` (int?, default حسب المصدر), `pageSize` (int?, default حسب المصدر).

Response المعلن: `AddressCoveragePreviewDto` — Status200OK.

## POST /api/v1/customer/family-subscription-flow/members/{memberId:guid}/addresses/{addressId:guid}/set-default

Body: لا يوجد body معلن.

Response المعلن: `CustomerAddressDto` — Status200OK.

## POST /api/v1/customer/family-subscription-flow/members

Body: `FamilyMemberWriteDto` (JSON في مرجع النوع).

Response المعلن: `FamilyGroupScreenDto` — Status200OK.

## PUT /api/v1/customer/family-subscription-flow/members/{memberId:guid}

Body: `FamilyMemberWriteDto` (JSON في مرجع النوع).

Response المعلن: `FamilyGroupScreenDto` — Status200OK.

## DELETE /api/v1/customer/family-subscription-flow/members/{memberId:guid}

Body: لا يوجد body معلن.

Response المعلن: `FamilyGroupScreenDto` — Status200OK.

## PUT /api/v1/customer/family-subscription-flow/members/{memberId:guid}/plan

Body: `SetFamilyMemberPlanRequest` (JSON في مرجع النوع).

Response المعلن: `FamilyGroupScreenDto` — Status200OK.

## GET /api/v1/customer/family-subscription-flow/summary

Body: لا يوجد body معلن.

Query: `memberId` (Guid?, default حسب المصدر).

Response المعلن: `FamilyCheckoutSummaryDto` — Status200OK.

## GET /api/v1/customer/family-subscription-flow/payment-methods

Body: لا يوجد body معلن.

Query: `amount` (decimal, default حسب المصدر).

الاستجابة: راجع شرح العملية؛ بعض العمليات ترجع anonymous JSON أو قائمة.

## POST /api/v1/customer/family-subscription-flow/checkout

Body: `FamilyStartCheckoutRequest` (JSON في مرجع النوع).

Response المعلن: `FamilyCheckoutStartedDto` — Status200OK.

## POST /api/v1/customer/family-subscription-flow/renewals/{renewalRequestId:guid}/checkout

Body: `FamilyRenewalCheckoutRequest` (JSON في مرجع النوع).

Response المعلن: `FamilyCheckoutStartedDto` — Status200OK.

## GET /api/v1/customer/family/home

Body: لا يوجد body معلن.

الاستجابة: راجع `GetFamilyHomeQuery/Command` والـDTO المرتبط في ملف 03.

## GET /api/v1/customer/family/members

Body: لا يوجد body معلن.

الاستجابة: راجع `GetFamilyMembersQuery/Command` والـDTO المرتبط في ملف 03.

## GET /api/v1/customer/family/members/{memberId:guid}

Body: لا يوجد body معلن.

الاستجابة: راجع `GetFamilyMemberQuery/Command` والـDTO المرتبط في ملف 03.

## POST /api/v1/customer/family/members

Body: `CreateFamilyMemberRequest` (JSON في مرجع النوع).

الاستجابة: راجع `CreateFamilyMemberQuery/Command` والـDTO المرتبط في ملف 03.

## GET /api/v1/customer/family/members/{memberId:guid}/capabilities

Body: لا يوجد body معلن.

الاستجابة: راجع `GetMyFamilyCapabilitiesQuery/Command` والـDTO المرتبط في ملف 03.

## GET /api/v1/customer/family/members/{memberId:guid}/access

Body: لا يوجد body معلن.

الاستجابة: راجع `GetFamilyMemberAccessQuery/Command` والـDTO المرتبط في ملف 03.

## PUT /api/v1/customer/family/members/{memberId:guid}/access

Body: `UpdateFamilyMemberAccessRequest` (JSON في مرجع النوع).

الاستجابة: راجع `UpdateFamilyMemberAccessQuery/Command` والـDTO المرتبط في ملف 03.

## GET /api/v1/customer/family/members/{memberId:guid}/permissions

Body: لا يوجد body معلن.

الاستجابة: راجع `GetFamilyMemberPermissionsQuery/Command` والـDTO المرتبط في ملف 03.

## PUT /api/v1/customer/family/members/{memberId:guid}/permissions

Body: `UpdateFamilyMemberPermissionsRequest` (JSON في مرجع النوع).

الاستجابة: راجع `UpdateFamilyMemberPermissionsQuery/Command` والـDTO المرتبط في ملف 03.

## POST /api/v1/customer/family/invites/{token}/accept

Body: لا يوجد body معلن.

الاستجابة: راجع `AcceptFamilyInviteQuery/Command` والـDTO المرتبط في ملف 03.

## PUT /api/v1/customer/subscriptions/{id:guid}/delivery-address

Body: `FamilyDeliveryAddressRequest` (JSON في مرجع النوع).

الاستجابة: راجع شرح العملية؛ بعض العمليات ترجع anonymous JSON أو قائمة.

## GET /api/v1/customer/subscriptions/{id:guid}/delivery-address

Body: لا يوجد body معلن.

الاستجابة: راجع شرح العملية؛ بعض العمليات ترجع anonymous JSON أو قائمة.

## GET /api/v1/customer/subscriptions/current/conversion-options

Body: لا يوجد body معلن.

Response المعلن: `SubscriptionConversionOptionsDto` — Status200OK.

## GET /api/v1/customer/subscriptions/management

Body: لا يوجد body معلن.

Query: `includeHistory` (bool, default true).

Response المعلن: `CustomerSubscriptionManagementScreenDto` — Status200OK.

## GET /api/v1/customer/subscriptions/current

Body: لا يوجد body معلن.

Response المعلن: `CustomerCurrentSubscriptionScreenDto` — Status200OK.

## GET /api/v1/customer/subscriptions

Body: لا يوجد body معلن.

Query: `includeHistory` (bool, default true).

الاستجابة: راجع شرح العملية؛ بعض العمليات ترجع anonymous JSON أو قائمة.

## GET /api/v1/customer/subscriptions/uncompleted

Body: لا يوجد body معلن.

الاستجابة: راجع شرح العملية؛ بعض العمليات ترجع anonymous JSON أو قائمة.

## GET /api/v1/customer/subscriptions/archived

Body: لا يوجد body معلن.

الاستجابة: راجع شرح العملية؛ بعض العمليات ترجع anonymous JSON أو قائمة.

## DELETE /api/v1/customer/subscriptions/{id:guid}

Body: لا يوجد body معلن.

الاستجابة: راجع شرح العملية؛ بعض العمليات ترجع anonymous JSON أو قائمة.

## GET /api/v1/customer/invoices

Body: لا يوجد body معلن.

الاستجابة: راجع شرح العملية؛ بعض العمليات ترجع anonymous JSON أو قائمة.

## GET /api/v1/customer/invoices/{receiptId:guid}

Body: لا يوجد body معلن.

Response المعلن: `CustomerSubscriptionInvoiceDto` — Status200OK.

## GET /api/v1/customer/payment-methods/default

Body: لا يوجد body معلن.

Response المعلن: `CustomerDefaultPaymentMethodDto` — Status200OK.

## GET /api/v1/customer/subscriptions/freeze-policy

Body: لا يوجد body معلن.

Response المعلن: `CustomerSubscriptionFreezePolicyDto` — Status200OK.

## GET /api/v1/customer/subscriptions/{id:guid}/upgrade-options

Body: لا يوجد body معلن.

Response المعلن: `CustomerUpgradeOptionsDto` — Status200OK.

## GET /api/v1/customer/subscriptions/{id:guid}/upgrade-summary

Body: لا يوجد body معلن.

Query: `targetTier` (RestaurantTier, default حسب المصدر).

Response المعلن: `CustomerUpgradeSummaryDto` — Status200OK.

## GET /api/v1/customer/subscriptions/{id:guid}/upgrade-payment-methods

Body: لا يوجد body معلن.

Query: `amount` (decimal?, default حسب المصدر).

الاستجابة: راجع شرح العملية؛ بعض العمليات ترجع anonymous JSON أو قائمة.

## GET /api/v1/customer/subscriptions/{id:guid}/payment-methods

Body: لا يوجد body معلن.

Query: `amount` (decimal?, default حسب المصدر).

الاستجابة: راجع شرح العملية؛ بعض العمليات ترجع anonymous JSON أو قائمة.

## POST /api/v1/customer/subscriptions/{id:guid}/upgrade-requests

Body: `CustomerUpgradeRequest` (JSON في مرجع النوع).

Response المعلن: `CustomerUpgradeStartedDto` — Status201Created.

## GET /api/v1/customer/subscriptions/{id:guid}/upgrade-requests/{upgradeId:guid}

Body: لا يوجد body معلن.

Query: `paymentId` (string?, default null).

Response المعلن: `CustomerUpgradeStatusDto` — Status200OK.

## GET /api/v1/customer/subscriptions/{id:guid}/management

Body: لا يوجد body معلن.

Response المعلن: `CustomerManagedSubscriptionDto` — Status200OK.

## GET /api/v1/customer/subscriptions/{id:guid}/invoices

Body: لا يوجد body معلن.

الاستجابة: راجع شرح العملية؛ بعض العمليات ترجع anonymous JSON أو قائمة.

## GET /api/v1/customer/subscriptions/current/freeze-details

Body: لا يوجد body معلن.

Response المعلن: `CustomerSubscriptionFreezeDetailsDto` — Status200OK.

## GET /api/v1/customer/subscriptions/{id:guid}/freeze-details

Body: لا يوجد body معلن.

Response المعلن: `CustomerSubscriptionFreezeDetailsDto` — Status200OK.

## POST /api/v1/customer/subscriptions/{id:guid}/freeze-requests

Body: `CustomerFreezeRequest` (JSON في مرجع النوع).

Response المعلن: `CustomerSubscriptionFreezeDetailsDto` — Status200OK.

## GET /api/v1/customer/subscriptions/{id:guid}/auto-renewal

Body: لا يوجد body معلن.

الاستجابة: راجع شرح العملية؛ بعض العمليات ترجع anonymous JSON أو قائمة.

## PUT /api/v1/customer/subscriptions/{id:guid}/auto-renewal/consent

Body: `AutoRenewalConsentRequest` (JSON في مرجع النوع).

الاستجابة: راجع شرح العملية؛ بعض العمليات ترجع anonymous JSON أو قائمة.

## DELETE /api/v1/customer/subscriptions/{id:guid}/auto-renewal/consent

Body: لا يوجد body معلن.

الاستجابة: راجع شرح العملية؛ بعض العمليات ترجع anonymous JSON أو قائمة.

## POST /api/v1/customer/subscriptions/{id:guid}/renewal-requests

Body: لا يوجد body معلن.

Response المعلن: `CustomerRenewalRequestDto` — Status202Accepted.

## GET /api/v1/customer/subscriptions/{id:guid}/cancellation-quote

Body: لا يوجد body معلن.

Response المعلن: `CustomerCancellationQuoteDto` — Status200OK.

## POST /api/v1/customer/subscriptions/{id:guid}/cancellation-requests

Body: `CustomerCancellationRequest` (JSON في مرجع النوع).

Response المعلن: `CustomerCancellationResultDto` — Status200OK.

## GET /api/v1/customer/subscriptions/{subscriptionId:guid}/calendar

Body: لا يوجد body معلن.

Response المعلن: `CustomerCalendarScreenDto` — Status200OK.

## GET /api/v1/customer/subscriptions/{subscriptionId:guid}/days/{date}

Body: لا يوجد body معلن.

Response المعلن: `CustomerCalendarDayDto` — Status200OK.

## GET /api/v1/customer/subscriptions/{subscriptionId:guid}/days/{date}/restaurants

Body: لا يوجد body معلن.

Query: `query` (string?, default حسب المصدر), `page` (int?, default حسب المصدر), `pageSize` (int?, default حسب المصدر).

Response المعلن: `CustomerDayRestaurantsDto` — Status200OK.

## GET /api/v1/customer/subscriptions/{subscriptionId:guid}/days/{date}/restaurants/{restaurantId:guid}/meals

Body: لا يوجد body معلن.

Query: `slot` (string?, default حسب المصدر), `page` (int, default 1), `pageSize` (int, default 20).

Response المعلن: `CustomerDayRestaurantMealsDto` — Status200OK.

## GET /api/v1/customer/subscriptions/{subscriptionId:guid}/days/{date}/meals/available

Body: لا يوجد body معلن.

Query: `slot` (string?, default حسب المصدر), `page` (int, default 1), `pageSize` (int, default 20).

Response المعلن: `CustomerDayRestaurantMealsDto` — Status200OK.

## POST /api/v1/customer/subscriptions/{subscriptionId:guid}/days/{date}/restaurant

Body: `SelectDayRestaurantRequest` (JSON في مرجع النوع).

Response المعلن: `CustomerCalendarDayDto` — Status200OK.

## POST /api/v1/customer/subscriptions/{subscriptionId:guid}/days/{date}/meals/slot

Body: `SelectDaySlotMealRequest` (JSON في مرجع النوع).

Response المعلن: `CustomerCalendarDayDto` — Status200OK.

## POST /api/v1/customer/subscriptions/{subscriptionId:guid}/days/{date}/complete

Body: `CompleteDayBoxRequest?` (JSON في مرجع النوع).

Response المعلن: `CustomerCalendarDayDto` — Status200OK.

## POST /api/v1/customer/subscriptions/{subscriptionId:guid}/days/{date}/confirm

Body: لا يوجد body معلن.

Response المعلن: `CustomerCalendarDayDto` — Status200OK.

## POST /api/v1/customer/subscriptions/{subscriptionId:guid}/days/{date}/auto-select

Body: لا يوجد body معلن.

Response المعلن: `CustomerCalendarDayDto` — Status200OK.

