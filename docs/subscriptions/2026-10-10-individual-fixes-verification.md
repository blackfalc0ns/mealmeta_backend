# Individual subscription backend verification — 2026-10-10

Implemented: disabled legacy individual tier cards and availability metadata; actual nutrition targets; automatic safe restaurant alternatives after capacity rejection; capacity exhaustion escalation and customer inbox/push handlers; freeze preparation using genuine menus for replacement dates, actual coverage/preferences/working/capacity checks, transactional reservation swap and unconfirmed replacement days. Ordinary 72-hour and first-day payment+24-hour rules preserved.

Historical repair tooling now requires reviewed subscription/day/stamp identity, current evidence under row locks, PostgreSQL execution strategy and explicit stored per-day payment-stamp provenance. A generic fulfillment audit is not enough. No production database changes were applied. Ambiguous historical stamps remain protected and require genuine evidence before repair.

Verification:
- Full UnitTests: 2031 passed, 0 failed/skipped.
- Scoped ApiTests: CustomerCheckout, CustomerCalendar and OrderLifecycle: 240 passed, 0 failed/skipped. Includes GoldenPath payment → cutoff → restaurant order → driver/outbox scenario previously failing CI.
- PostgreSQL Capacity integration tests: 12 passed, 0 failed/skipped. Includes customer/admin freeze last-box races, automatic alternative and concurrent last-box transfer.
- Arabic/English resource tests included in API suite. i18n audit self-test passed; prior diagnostic scan zero key parity mismatches. Mobile runtime and live language switching were not executed.
- Scoped whitespace checks and production source review completed. Existing unrelated build warnings remain; unrelated local changes were preserved.

Independent review corrected documented candidate JSON casing and immediate freeze approval handoff; hardened audit source proof and connected replacement notification consumers. No full API suite or hosted deployment/runtime health check claimed.

Mobile integration document: mobile-individual-subscription-handoff.md. It covers actual routes, availability precedence, canEdit/deadlines, readiness/payment/fulfillment, automatic failures, freeze status, Arabic/English semantic copy and acceptance scenarios. Flutter source changes are outside this request.

Remaining operational acceptance: confirm deployed dev build, hosted lock job/outbox/FCM configuration, genuine historical stamp evidence, then execute the mobile acceptance checklist. Backend-only automated success does not certify all real users and integrations 100%.

Mock Data Check:
- No mock business data introduced.
- No fake fallback responses introduced.
- No hardcoded production business values introduced.
- All runtime data comes from real application sources.
