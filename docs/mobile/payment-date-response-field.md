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

Requests still send `startDate` as before. In responses, `startDate` remains the
computed first delivery date, and summary `endDate` remains the last service date.
Neither delivery calculations nor retry/resume behavior changes.

Example individual summary date fields for six delivery days, excluding Friday:

```json
{
  "paymentDate": "2026-10-10",
  "startDate": "2026-10-14",
  "endDate": "2026-10-20",
  "durationDays": 6,
  "includeFriday": false,
  "serviceDates": [
    "2026-10-14", "2026-10-15", "2026-10-17",
    "2026-10-18", "2026-10-19", "2026-10-20"
  ]
}
```
