# Influencer / Partner Subsystem Design Spec
## Complete Screen Inventory, Navigation Flows, Financial Zero-Loss Engine, and API Contracts

> **Date:** 2026-09-09  
> **Status:** Validated & Approved Spec  
> **Location in Mobile Docs:** [`f:\MealMate\backend\docs\mobile\influencer-partner-mobile-spec.md`](file:///f:/MealMate/backend/docs/mobile/influencer-partner-mobile-spec.md)

Refer to the primary comprehensive specification document located at:
[`f:\MealMate\backend\docs\mobile\influencer-partner-mobile-spec.md`](file:///f:/MealMate/backend/docs/mobile/influencer-partner-mobile-spec.md)

### Key Architecture Highlights:
1. **Admin-driven Promotion**: Admin promotes user by phone number from Admin Dashboard (`PromoteCustomerToInfluencerCommand`), granting `isInfluencer: true` and a dedicated promo code (`AMMAR20` / `Mealmate20`).
2. **Dual Mode Experience**: Customer remains a regular user who can subscribe, order, and manage addresses. Banner in profile enables seamless 1-tap switching into the Influencer Dashboard shell (5 tabs) and 1-tap switching back to Customer Mode.
3. **Zero-Loss Financial Guard**:
   - Commissions are marked as `Pending` until the customer's first meal is delivered (or after 48h active plan).
   - Cancellations prior to delivery result in in-app wallet credit (no cash outflow) and automatic cancellation of pending influencer commission.
   - Any post-delivery cancellations charge the customer full non-discounted daily rates + cancellation fees, eliminating platform loss.
   - Withdrawals require admin approval, have a minimum limit (10 KWD), and permit only a single active pending request at a time.
4. **All 11 Screens Fully Mapped**:
   - Customer Profile banner
   - Social Connect (6 channels: TikTok, Instagram, WhatsApp, Telegram, YouTube, Snapchat)
   - Influencer Tab 1: Home (KPIs, code card, live activity feed)
   - Influencer Tab 2: Links (Referral deep link, QR code, 1-tap social share, conversion stats)
   - Influencer Tab 3: Analytics (Time filters 7d/30d/90d/6m/1y, line chart, bar chart, top packages)
   - Influencer Tab 4: Wallet (Available balance, pending balance, lifetime earnings, monthly breakdown, transaction history)
   - Withdrawal Subflow: Form -> Review Modal -> Success Screen -> History List (Tabs: All/Pending/Transferring/Completed/Rejected) -> Transaction Details.
   - Influencer Tab 5: Profile (Bank management, social links edit, terms, switch to customer mode).
