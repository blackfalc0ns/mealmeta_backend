# Design Spec: Mobile Calendar, Meal/Restaurant Selection, Lead Times & Admin Compensation

## 1. Objective & Scope
Implement the end-to-end Mobile Calendar and Subscription lifecycle flow for MealMate:
- **Lead Time & First Delivery**: Subscription starts after a configurable lead time buffer (default 48h to 72h from payment), customizable by Admin.
- **Meal & Restaurant Selection with Diversification Rules**:
  - Customer selects meals according to their bundle/subscription capacity (e.g. 2 meals + 1 snack).
  - Dynamic diversification quota calculation: $L_{max} = \lceil \frac{D_{total}}{R} \rceil + \text{Margin}$ (where $R=1 \implies 100\%$ days allowed).
  - Persist snapshot into `DiversificationLimitSnapshot`.
- **Allergy & Preference Filtering + Smart Auto-Selection**:
  - Menu lists exclude or warn about dishes containing customer allergens (`CustomerAllergyPreference`).
  - If a customer fails to select meals before daily cut-off / lock time (`LockAtUtc`), system runs smart auto-selection adhering to customer preferences & allergen safety.
- **Restaurant Confirmation SLA & Admin Compensation**:
  - Daily order dispatch to restaurant at lock cut-off time.
  - If restaurant fails to confirm within SLA window, flag order as `RestaurantUnresponsive` and trigger urgent admin escalation.
  - Admin compensation actions: (1) Reassign to alternative approved restaurant, (2) Credit customer wallet, (3) Extend subscription (+1 day).
- **Mobile Screens (UI/UX)**:
  - 05.10: Meal Calendar with day strip, cut-off countdown timer, selection trigger.
  - 05.11: Restaurant Browser with search, kitchen filters, ratings, remaining days quota badge.
  - 05.12: Restaurant Profile & Categorized Menu (Breakfast, Lunch, Dinner, Snacks) with ingredients origin badges.
  - 05.14: Meal Nutrition Details modal with macros (protein, carbs, fat, fiber, calories donut charts) and allergen tags.

---

## 2. Mathematical Diversification Formula
For subscription with duration $D$ days and eligible restaurant pool size $R$:
$$\text{MaxDaysPerRestaurant}(D, R) = \begin{cases} D & \text{if } R \le 1 \\ \min\left(D, \max\left(1, \lceil \frac{D}{R} \rceil + 1\right)\right) & \text{if } R > 1 \end{cases}$$

---

## 3. Data & State Machine
### SubscriptionDay Status Transitions
- `Open` $\rightarrow$ Customer selects $\rightarrow$ `CustomerSelected`
- `Open` (Timeout at `LockAtUtc`) $\rightarrow$ Auto-Selection $\rightarrow$ `AutoSelected`
- `CustomerSelected` / `AutoSelected` $\rightarrow$ Locked for prep $\rightarrow$ `SentToRestaurant`
- `SentToRestaurant` $\rightarrow$ Restaurant confirms $\rightarrow$ `ConfirmedByRestaurant` $\rightarrow$ `InPreparation` $\rightarrow$ `OutForDelivery` $\rightarrow$ `Delivered`
- `SentToRestaurant` (SLA timeout without confirmation) $\rightarrow$ `RestaurantUnresponsive`
- `RestaurantUnresponsive` $\rightarrow$ Admin Reassign $\rightarrow$ `ReplacementWindowOpen` / `Reassigned`
- `RestaurantUnresponsive` $\rightarrow$ Admin Wallet Refund $\rightarrow$ `CompensatedWithWallet`
- `RestaurantUnresponsive` $\rightarrow$ Admin +1 Day Extension $\rightarrow$ `CompensatedWithExtension`
