# Implementation plan
1. Add failing Payzah receipt/verification tests and record red run.
2. Introduce neutral contracts, new encrypted settings and persisted attempt model.
3. Implement documented initialization/status transport and verified callback flow.
4. Convert checkout/family/upgrade consumers to explicit receipts and unique attempt identity.
5. Remove old active provider code/configuration; migrate settings forward without copying old credentials; protect historical refund operations.
6. Update admin and payment contracts; run relevant backend/admin verification and review diff.

## Verification evidence

- Meaningful red: adapter redirect-fragment test failed (1 failed, 4 passed) before fragment/user-info rejection implementation. Initial test invocation was blocked independently by preexisting voice CS8954.
- Actual UnitTests: 47 passed / 0 failed. Filter `PayzahReceiptTests|SubscriptionUpgradePaymentProcessorTests|SubscriptionFlowGapTests|SandboxCheckoutTests|SubscriptionPurchaseAndSnapshotTests`.
- Additional red: missing-verifier confirmation test failed before deletion of the legacy direct-confirmation fallback. Confirmation now fails closed without verifier and does not mutate subscription, holds or receipts.
- Actual ApiTests: 61 passed / 0 failed. Filter `PayzahPaymentReturnControllerTests|CustomerCheckoutControllerTests|CustomerSubscriptionFlowControllerTests|CustomerCheckoutPaymentStatusTests|CustomerCheckoutGateControllerTests|CheckoutCouponFlowTests`.
- API built with a temporary MSBuild override that splits the preexisting duplicate-file-scoped-namespace voice source into two temporary compile inputs. The user's original source was not changed. Ordinary build remains blocked by that unrelated CS8954.
- Admin type check and Angular build passed in primary agent review.
- Migration contains only two new gateway tables/indexes, removal of old credential config, deactivation of old accounting gateway and legacy pending-refund review state. Snapshot/Designer preserve the last preexisting model plus gateway blocks, excluding unrelated model drift. No migration was applied.
- Test-only HTTP handlers supply provider fixtures. No live provider requests, purchases, refunds or deployment were performed. Merchant sandbox validation remains required.

## Allowed changed-file manifest

- Backend production: neutral payment abstraction; Payzah adapter; Payzah config/settings/bootstrap; new PaymentGatewayAttempt and EF config/context; payment processor and individual/family/upgrade consumers; DI/API callbacks/settings/controllers/localization/config/resources; refund operation default and gateway protection.
- Backend persistence: new `20261006200145_ReplaceActiveGatewayWithPayzah` migration/Designer and gateway-only snapshot delta. Old applied migrations retained.
- Backend tests: renamed/retired obsolete provider tests; new adapter/return/processor regressions; payment contract fixture updates in customer checkout/subscription flow/upgrade/sandbox/order-lifecycle and integration checkout tests. Parallel scratch provider substitutions were reversed; no scratch files are part of this implementation.
- Backend deployment: removed old diagnostics-only workflow job/inputs and its provider script; existing deployment stages retained.
- Admin: gateway API/page/navigation and provider-neutral missing historical payment-method label.
- Documentation: this plan/spec; current Payzah contract mirrored in backend/root/admin mobile docs; superseded-payment-section notices on mixed mobile guides; active OpenAPI settings schema/provider routes refreshed. Historical specs and provider records retained.

Mock Data Check:
- No mock business data introduced.
- No fake fallback responses introduced.
- No hardcoded production business values introduced.
- All runtime data comes from real application sources.
