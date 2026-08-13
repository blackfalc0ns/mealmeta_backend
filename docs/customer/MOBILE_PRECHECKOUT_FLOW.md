# Mobile pre-checkout contract

## Compatibility

- During rollout, parse enum fields as either a JSON string or an integer. The API now emits strings such as `Active`, `Basic`, and `PendingPayment`.
- `/menu-plans` is the canonical menu route. `/menu-cycles` remains available temporarily and returns `Deprecation: true`.
- Do not calculate price, eligibility, allergens, or available meals on the device. Render the API result.

## Food preferences

Load all choices from `GET /api/v1/customers/me/food-preferences/catalog`.

The response contains:

- `ingredients`: only active ingredients supplied by approved operational restaurants.
- `allergens`: canonical codes currently used by those ingredients.
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

After payment, calendar and available-meal endpoints read immutable daily snapshots. Calendar days include `selectedMealIds`, `isAutoSelected`, `selectionDeadlineUtc`, and `hasMealSnapshot`. Available meals include the frozen description, image, macros, direct/possible allergens, dietary codes, and ingredient origins. A meal can contain `blockReason` values such as `allergy_conflict`, `ingredient_preference_conflict`, or `dietary_preference_conflict`; disable that meal and display the localized reason.

Send at most one meal per slot. The customer may save a partial selection before `selectionDeadlineUtc`; the worker fills missing slots from the same immutable snapshot and never reads the restaurant's live menu. A legacy day without a resolvable snapshot returns no available meals and is reported to operations instead of guessing.

Never retry payment confirmation from the mobile app. MyFatoorah webhook confirmation is idempotent; a late payment after hold expiry is routed to financial review.
