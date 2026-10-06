# Calendar editing until restaurant confirmation

For customer calendar summaries, day details, and selection command responses:

- `SentToRestaurant` with no restaurant confirmation returns `canEdit: true` and `uiStatus: "awaiting_restaurant"` only before `selectionDeadlineUtc`.
- Editing closes at `selectionDeadlineUtc` (72 hours before service under the configured 72-hour policy), or when the restaurant confirms, whichever happens first. An unconfirmed day ten days ahead remains editable even if already sent to the restaurant. Use `canEdit` to enable selection controls.
- Editing a meal keeps the day `SentToRestaurant` and the existing order awaiting confirmation. Restaurant reads use the updated persisted selections.
- Changing restaurant before the deadline clears the old meals, moves the pending daily order to the new restaurant, and returns the day to `Open`. The customer can fill and complete the new box before the deadline, then confirm it to send it again. A previously dispatched day is sent again even before the deadline. The old restaurant cannot confirm the reassigned day.
- `ConfirmedByRestaurant`, `Preparing`, `OutForDelivery`, and `Delivered` remain read-only. Frozen, cancelled, and failed days remain read-only.
- Existing unconfirmed `SentToRestaurant` days receive the new behavior without a data migration.
- A confirmation timestamp left over from an earlier attempt does not lock a `SentToRestaurant` day. Dispatch, replacement, and valid customer edits clear the old timestamp; the current day status determines whether the restaurant has confirmed this attempt.

Deploy the updated backend and reload the calendar/day response for mobile clients to receive these values.
