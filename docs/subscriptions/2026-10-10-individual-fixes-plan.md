# Individual subscription fixes — 2026-10-10

Scope authorized: backend fixes for audited subscription gaps plus mobile integration handoff; work directly on dev, preserve unrelated edits. First day alone retains 24 hours from payment; subsequent days enforce at least 72 hours.

1. Automatic meal selection: try eligible alternatives after capacity rejection; escalate exhausted capacity; use actual nutrition targets rather than fixed calorie reference. Add regression tests.
2. Legacy subscription discovery: retain unavailable tiers with metadata; document address coverage and whole-period checks. Add compatibility regressions.
3. Freeze: do not confirm new replacement days; release old holds, prepare date-specific verified menus and reserve replacement capacity atomically. Reject unavailable replacement preparation with explicit reasons. Regression tests for freeze/approval and rollback.
4. Historical data: diagnostic and evidence-based repair only, never erase legitimate confirmation blindly.
5. Mobile handoff: endpoints, availability precedence, deadline/first-day rules, empty/error states, payment and freeze behavior, Arabic/English semantic copy and acceptance scenarios.
6. Verify focused unit/API tests, review scoped changes, push only authorized files to dev.

Runtime mobile and hosted job behavior cannot be certified by backend-only tests. Record actual evidence and limitations.
