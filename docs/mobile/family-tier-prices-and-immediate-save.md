# Family tiers and immediate preference saving

## Duration and prices

GET /api/v1/customer/family-subscription-flow/programs/{programId}/tiers
requires bundleId and familyMemberId for the intended family member.

Before choosing a duration, omit durationDays entirely. Do not send a default
12 or 26, a previously selected member's duration, or a cached duration.
The backend no longer selects a default duration for family tier prices.
The tier's durationDays, totalPrice, dailyPrice, priceDifference, currencyCode,
fridayDays and totalServiceDays are null until a duration is explicitly sent.
durationPrices is the available pricing catalog, not a selected price:
do not show its first entry as the current price.

After choosing/changing a duration, fetch tiers again with that durationDays,
includeFriday, programId, bundleId and familyMemberId. Display totalPrice from
that response. Match a locally used durationPrices entry by durationDays;
never use a fixed index. For includeFriday=true, refetch the server-calculated
totalPrice instead of reusing the catalog values without Friday.

Serialize requests, or discard an older response if its member, program,
bundle, duration or Friday option no longer matches the current selection.
Clear the displayed price while the matching request is loading.
An HTTP GET cannot tell whether the mobile user chose a value or the client
inserted a default: sending durationDays=12 explicitly requests a 12-day price.

## Save each selection without a confirmation button

Send these authenticated PUT requests immediately after each toggle, with
the complete current selection, including an empty list when clearing it.
The server saves within the PUT request; there is no confirmation endpoint.

PUT /api/v1/customer/family-subscription-flow/members/{memberId}/allergies

```json
{"selectedCodes":["MILK","PEANUTS"]}
```

PUT /api/v1/customer/family-subscription-flow/members/{memberId}/dietary

```json
{"selectedCodes":["VEGETARIAN"]}
```

Codes above show the request shape. Send only the actual codes returned by
the catalog for this environment; do not invent preference codes.

Clearing all selections:

```json
{"selectedCodes":[]}
```

Successful PUT currently returns true. Restore state from the respective GET
member endpoint or GET /api/v1/customer/family-subscription-flow/current.
The backend stores selected rows and completion state before returning.

Use a separate ordered save queue per member and section. Coalesce pending
toggles into the latest full selection; do not issue overlapping PUT requests
whose older payload can arrive last and overwrite the newer selection.
Wait for pending saves when leaving the screen. On failure keep the unsaved
selection visible and offer retry; do not show it as saved.
Switching member must restore that member's state, not the manager's cached
list. Flutter source is not present in this backend repository, so the
per-toggle event handler and request queue must be connected in the mobile app.

## Backend regression verification

FamilyTierPrices_AreAbsentUntilDuration_AndChangeWithRequestedDuration covers
absent duration, explicit 6/12 days and exact prices from the pricing handler.
MemberPreferences_EachSelectionPersistsImmediatelyWithoutConfirmation covers
adding, removing and clearing allergies/dietary selections, clearing EF's
change tracker and reading persisted rows and GET responses after each PUT.
These are backend tests using the isolated EF test database, not evidence
that the Flutter on-change handlers issue the requests or that the reported
customer's live session was tested.
