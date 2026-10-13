# Family summary restaurant data

`GET /api/v1/customer/family-subscription-flow/summary` returns `restaurants` and
`restaurantRepetition` for `selectedMemberId`. Switching the selected member
changes these fields to that member's delivery address, plan and preferences.
The same data remains available per member in `memberSchedules`.

Restaurant cards contain `restaurantId`, `nameAr`, `nameEn`, `logoUrl`, `tier`
and `occurrenceCount`. Names come from business records. Logos use the same
document/branch image resolver as the individual summary.

`restaurantRepetition` has the same contract and calculator as individual
subscriptions: `limit`, `restaurantCount`, `durationDays`. `occurrenceCount`
exposes that maximum selection limit for each restaurant; it is not a count
of already scheduled orders. The limit depends on subscription duration and
the complete eligible restaurant set, rather than assigning the entire
duration to every restaurant. No eligible restaurants means an empty array
and a zero limit.

Eligibility uses the normalized first delivery date and the exact dates shown
in the member schedule, including Friday selection. Ineligible restaurants
remain excluded. A genuinely unavailable period still returns an empty list.

Arabic label for the limit: الحد الأقصى لتكرار المطعم.
English label for the limit: Maximum restaurant selections.
Client UI copy must be stored in the application's localization resources.

The summary and each member schedule also expose `availability`, using the
individual availability contract: `status`, `coveredRestaurantCount`,
`eligibleRestaurantCount`, `checkedAtUtc`, `effectiveStartDate`, `messageCode`
and `failureReasons`. Failure reason counts come from the real eligibility
evaluation (for example menu, capacity, tier or coverage); no restaurant is
invented or admitted to bypass a failed eligibility check. Missing dates and
missing delivery regions are reported as incomplete selection/address.
