# Mobile pre-checkout contract

## Compatibility

- During rollout, parse enum fields as either a JSON string or an integer. The API now emits strings such as `Active`, `Basic`, and `PendingPayment`.
- `/menu-plans` is the canonical menu route. `/menu-cycles` remains available temporarily and returns `Deprecation: true`.
- Do not calculate price, eligibility, allergens, or available meals on the device. Render the API result.

## Food preferences

Load all choices from `GET /api/v1/customers/me/food-preferences/catalog`.

The response contains:

- `ingredients`: only active ingredients supplied by approved operational restaurants.
- `allergens`: the full active EU14 catalog from `AllergenDefinitions`. Display `nameAr`/`nameEn` as short labels (`جلوتين`, `لاكتوز`, `مكسرات`), never the long legal names.
- `dietaryPreferences`: admin-managed codes such as `VEGETARIAN`, `KETO`, `LOW_CARB`, and `LACTOSE_FREE`.

Save page-specific choices with:

- `PUT /api/v1/customers/me/food-preferences/allergies` body `{ "allergies": [{ "code": "GLUTEN", "severity": null }] }`
- `PUT /api/v1/customers/me/food-preferences/dietary` body `{ "dietaryPreferenceCodes": ["KETO"] }`
- `PUT /api/v1/customers/me/food-preferences/disliked-ingredients` body `{ "ingredientCodes": ["INGREDIENT:<guid>"] }`

`GET /api/v1/customers/me/food-preferences` now also returns `dietaryPreferenceCodes`. The existing `dietaryPattern`, `allergies`, `dislikedFoods`, and `notes` fields remain.

## Checkout

1. Load `GET /api/v1/customer/checkout/catalog`. Only active programs and bundles are returned.
2. Load `GET /api/v1/customer/checkout/restaurants` using program, bundle, tier, duration, start date, region, and Friday selection. Only fully eligible restaurants are returned.
3. Start payment with `POST /api/v1/customer/subscriptions/checkout`.

The checkout response retains `menuPlanVersionId` for the first service day and adds:

- `priceSnapshotId`
- `eligibilityCheckedAtUtc`
- `menuPlanVersionsByDate`: `{ serviceDate, menuPlanVersionId }[]`
- `restaurantPoolSnapshotId`
- `restaurantSnapshotCount`
- `paymentStatus`: initially `PendingPayment`
- `fulfillmentStatus`: initially `PendingPayment`

The `callbackUrl` and `errorUrl` must use an allowed HTTPS host or the configured `mealmate://` application scheme. Do not send arbitrary redirect URLs.

## Payment and frozen restaurant pool

The MyFatoorah callback does not mean that the calendar is ready. The backend verifies the Payment ID, Invoice ID, customer reference, amount, currency, and paid status first. It then creates all subscription days and copies their immutable meal snapshots. Until that finishes, `GET /api/v1/customer/subscriptions/{id}/calendar` returns `409 calendar.fulfillment_not_ready` with `paymentStatus`, `failureReasonCode`, and `isFulfillmentReady: false`. The app may retry this GET with backoff; it must never call payment confirmation itself.

`GET /api/v1/customer/subscriptions/{id}/restaurants` returns the frozen restaurant pool captured at checkout. Use this response for subscription restaurant screens. Never merge it with live checkout restaurants: a restaurant added later is intentionally absent from the old subscription.

After payment, calendar and available-meal endpoints read immutable daily snapshots. Calendar days include `selectedMealIds`, `isAutoSelected`, `selectionDeadlineUtc`, and `hasMealSnapshot`. Available meals include the frozen description, image, macros, direct/possible allergens, dietary codes, and ingredient origins. A meal can contain `blockReason` values such as `allergy_conflict`, `ingredient_preference_conflict`, or `dietary_preference_conflict`; disable that meal and display the localized reason.

Read `slotRequirements` from every calendar day. Each item has `{ "slot": "lunch", "count": 2 }`. Allow up to that count for the slot; do not assume one meal per slot. The customer may save a partial selection before `selectionDeadlineUtc`; `POST .../auto-select` keeps valid existing choices and fills only the missing quantity from the same immutable snapshot. If current allergies/preferences leave too few safe meals, the day becomes `ReplacementRequired`.

Never retry payment confirmation from the mobile app. MyFatoorah webhook confirmation is idempotent; a late payment after hold expiry is routed to financial review.
