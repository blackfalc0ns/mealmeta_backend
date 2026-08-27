# Active delivery address delete guard + 72h change notice

**Status:** accepted  
**Date:** 2026-08-27  
**Scope:** Customer addresses API (`/api/v1/customers/me/addresses`)

## Context

Delivery address changes already schedule a 72-hour delay before the new address becomes the active delivery target (`CustomerDeliveryAddressService`). Boxes due during that window keep using the current active delivery address.

Product rules clarified:

1. The address currently used for delivery must not be deletable until another address becomes active after the 72-hour delay.
2. When the customer changes / sets a new default delivery address, the API must return a clear notice that the new address will not receive deliveries until after 72 hours.

## Decision

### Delete guard (Approach 1)

- Block `DELETE` only when `address.Id == profile.ActiveDeliveryAddressId`.
- Return `Conflict` with code `address.active_delivery_cannot_delete` and a bilingual user-facing message explaining they must set another default address and wait for it to activate after 72 hours.
- Non-active addresses remain freely deletable, including a pending (scheduled) address.
- Deleting a pending address clears `PendingDeliveryAddressId` and `PendingDeliveryAddressEffectiveFromUtc` on the profile so the schedule does not point at a missing row.
- Do **not** key the guard off `IsDefault` alone: default flips immediately, while active delivery stays on the previous address for 72 hours.

### 72-hour change notice

- Keep scheduling via `ScheduleDeliveryChangeAsync` when creating/updating makes a new default or changes content of the active/pending delivery address.
- Ensure create/update responses include `deliveryChangeNotice` (and `deliveryEffectiveFromUtc`) whenever the returned address is the pending delivery target.
- Notice meaning (AR/EN): the new address is saved but will not activate for deliveries until after 72 hours; boxes before then still go to the current active delivery address.
- Tighten copy if needed so it explicitly says the address will not become active / receive orders until after 72 hours.

### Out of scope

- Flutter UI changes (mobile already can show `deliveryChangeNotice` from the DTO).
- Soft-delete / archival of addresses.
- Changing the 72-hour delay duration.

## Data flow

1. Customer sets address B as default while A is active → pending = B, effective = now + 72h; response includes notice.
2. During the wait: DELETE A → conflict; DELETE B → allowed and clears pending; DELETE other unused addresses → allowed.
3. After effective time (on read/apply): active = B; DELETE A → allowed.

## Error contract

| Case | HTTP | Code |
|------|------|------|
| Delete active delivery address | 409 Conflict | `address.active_delivery_cannot_delete` |

## Testing

- Reject delete of active delivery address.
- Allow delete of non-active address.
- Allow delete of pending address and clear pending fields.
- After pending applies, former active address can be deleted.
- Switching default schedules 72h and returns `deliveryChangeNotice` containing the 72-hour activation message.

## Affected files

- `src/MealMate.Application/Features/IdentityAccess/Customers/CustomerAddresses.cs`
- `src/MealMate.Application/Features/IdentityAccess/Customers/CustomerDeliveryAddressService.cs` (notice text only if needed)
- `tests/MealMate.UnitTests/IdentityAccess/CustomerAddressesTests.cs`
