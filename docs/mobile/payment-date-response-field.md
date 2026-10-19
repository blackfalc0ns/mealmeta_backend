# Individual checkout response: paymentDate

The individual summary and checkout responses now expose `paymentDate` instead of
`chosenStartDate`. The old JSON field is removed; mobile clients must read the new
field. The family summary already exposes `memberSchedules[].paymentDate`.

Affected individual responses:

- `GET /api/v1/customer/subscription-flow/summary`
- Initial checkout and resumed checkout responses using `CheckoutStartedDto`.

This is a response-field rename only. The value remains the existing selected
checkout date/anchor, not the timestamp of a successful gateway payment. It does
not establish that payment succeeded. The family summary's existing `paymentDate`
continues to use the summary's current UTC date; this rename does not unify those
semantics.

Requests send `startDate` as the selected first delivery date. The current
individual schedule validates and keeps that supplied date; it does not add lead
days again. `purchase-options` calculates the earliest valid delivery date when
its optional date is absent. Pass the returned date unchanged to summary and
checkout. In this direct flow `paymentDate` is the selected date echo and equals
`startDate`; it is not a bank payment timestamp. `endDate` is the last service date.
See [the direct-payment contract](OFFER_DIRECT_PAYMENT_MOBILE_CONTRACT.md).

Example individual summary date fields for six delivery days, excluding Friday:

```json
{
  "paymentDate": "2026-10-26",
  "startDate": "2026-10-26",
  "endDate": "2026-11-01",
  "durationDays": 6,
  "includeFriday": false,
  "serviceDates": [
    "2026-10-26", "2026-10-27", "2026-10-28",
    "2026-10-29", "2026-10-31", "2026-11-01"
  ]
}
```
