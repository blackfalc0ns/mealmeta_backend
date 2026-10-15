# Family member independent access

The manager uses two separate actions:

* `PUT /api/v1/customer/family/members/{memberId}/access` provisions or disables the member's ordinary customer login. `generateTemporaryPassword` returns the password once and remains OTP gated. It does not detach the member.
* `POST /api/v1/customer/family/members/{memberId}/detach` makes the member independent from manager mutations. It is idempotent and requires an active linked customer login, an unpaused access row, and completed password setup. It preserves `FamilyGroupId`, `Subscription.FamilyGroupId`, `Subscription.FamilyMemberId`, subscription status, and subscription days.

After detachment the member remains visible in manager roster and detail projections, with calendar, plan, and invoice status available read-only. Manager mutation decisions return `family.member_detached`. A detached member becomes an independent customer with every supported own action, regardless of previous manager grants. The detach transaction enables supported own grants, clears sibling grants and unsupported legacy grants, and advances the permission version. Existing detached rows also receive effective own rights even when their stored flags are stale:

| Action | Detached member own session |
| --- | --- |
| View calendar, view plan, view invoices | Automatically allowed on own subscription |
| Select restaurant, edit daily meals, confirm meals | Automatically allowed, subject to day state and deadlines |
| Edit address, change password | Automatically allowed on own account/service |
| Renewal, freeze, upgrade, cancellation | Automatically allowed, subject to service policy |
| Family-scope sibling calendar, plan, invoices | Denied after detachment |
| Edit meals and days, change program, change delivery time, unit exchange | Currently unsupported and denied |

The member context from `GET /api/v1/customer/family/me` includes `isDetachedFromFamilyManagement` and capability decisions. The manager access response and manager roster/detail projections expose the same state as `detachedFromFamilyManagement` or `isDetachedFromFamilyManagement`, according to their DTO shape. Clients should render manager rows read-only when detached and use the capability reason codes for action affordances.

Detachment returns `family.checkout_pending` while the member is selected in a draft checkout, a family payment is pending or under review, a member tier upgrade is submitted, or a member renewal payment is pending or under review. Finish or resolve the financial operation before detaching. An unpaid renewal intent alone does not block detachment; starting its payment still checks the current actor's permissions.

For the account creation action, send `hasSeparateAccount: true` and `generateTemporaryPassword: true` to the access endpoint. Show `generatedPassword` only from that response; a later GET does not return it. This action preserves manager control and does not detach the member. The member uses the normal customer login, completes the configured OTP check and chooses a new password. Use `/family/me` capabilities for family access; generic login `roles` and `permissions` are not the family permission catalog.
