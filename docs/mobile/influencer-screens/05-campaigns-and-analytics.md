# شاشة 5 — التحليلات وأداء الحملات (05-campaigns-and-analytics)

**الملف السابق:** [04-social-links-and-profile.md](./04-social-links-and-profile.md) · **الرئيسية:** [README](./README.md)

---

## 🎯 الهدف من الشاشة

تزويد المؤثر والشريك التسويقي بتقارير تحليلية بيانية ورسوم تفاعلية تُمكّنه من قياس نجاح حملاته الترويجية وتحسين عوائده المالية:
1. تصفية الأداء حسب الفترات الزمنية (`7d` آخر أسبوع، `30d` آخر شهر، `90d` آخر 3 أشهر، `all` كل الفترات).
2. استعراض خط زمني بياني مرتب تصاعدياً (Ascending Timeline) مع ملء الأيام التي لا تحتوي مبيعات بقيم صفرية لضمان استمرارية الرسم البياني.
3. عرض نسب التغير والمقارنة المئوية (`comparison`: `clicksChangePercentage`, `ordersChangePercentage`, `netCommissionChangePercentage`).
4. تفكيك المبيعات والاشتراكات حسب أكثر الباقات والبرامج طلباً مع توفير `programId` و `bundleId`.

---

## 🖼️ المكونات البصرية للشاشة (UI Components)

### 1) شريط الفترات الزمنية (Time Period Segmented Control):
- أزرار تبديل سريعة: `7 أيام` · `30 يوم` (افتراضي) · `90 يوم` · `الكل`.
- إعادة جلب البيانات فور تغيير التبويب بسلاسة مع مؤشر تحميل خفيف.

---

### 2) كروت الملخص والمقارنة (Summary & Comparison Cards):
- **إجمالي النقرات (Total Clicks):** مع شارة نسبة التغير (مثال: `▲ +8.2%`).
- **الاشتراكات المحققة (Affiliated Orders):** مع شارة نسبة التغير (مثال: `▲ +5.1%`).
- **معدل التحويل (Conversion Rate %):** نسبة المشتركين إلى النقرات.
- **صافي العمولات (Total Net Commission):** بالدينار الكويتي `currencyCode` (مع شارة التغير `▲ +12.5%`).

---

### 3) الرسم البياني التفاعلي (Interactive Ascending Performance Chart):
- نقاط الرسم البياني (`earningsChart`) مرتبة تصاعدياً من التاريخ الأقدم إلى الأحدث، وتتضمن النقرات والاشتراكات والمبالغ لكل يوم:
  - الخط الأزرق: النقرات اليومية (`clicks`).
  - الخط الأخضر: الاشتراكات الفعلية اليومية (`orders`).
- شريط الأدوات المنبثق (Tooltip) عند الضغط على أي يوم لعرض تفاصيل ذلك اليوم بدقة.

---

### 4) جدول أكثر البرامج والوجبات مبيعاً (Top Converting Programs):
- قائمة بالباقات الأكثر اشتراكاً عبر كوده:
  - معرّف البرنامج (`programId`) واسم الباقة بالعربي والإنجليزي.
  - عدد المشتركين (`orderCount`).
  - إجمالي الإيرادات وإجمالي العمولة المكتسبة (`totalCommission`).

---

## 🌐 تفاصيل الـ APIs

### جلب بيانات التحليلات

```http
GET /api/v1/influencer-partner/me/analytics?period=30d
Authorization: Bearer <customer_jwt_token>
```

#### نموذج الاستجابة (200 OK):
```json
{
  "period": "30d",
  "currencyCode": "KWD",
  "granularity": "day",
  "totalEarnings": {
    "amount": 432.000,
    "currency": "KWD"
  },
  "comparison": {
    "clicksChangePercentage": 8.2,
    "ordersChangePercentage": 5.1,
    "netCommissionChangePercentage": 12.5
  },
  "earningsChart": [
    { "dateUtc": "2026-08-16T00:00:00Z", "amount": 0.000, "clicks": 0, "orders": 0 },
    { "dateUtc": "2026-08-17T00:00:00Z", "amount": 12.000, "clicks": 45, "orders": 2 },
    { "dateUtc": "2026-08-18T00:00:00Z", "amount": 18.000, "clicks": 60, "orders": 3 },
    { "dateUtc": "2026-08-19T00:00:00Z", "amount": 0.000, "clicks": 10, "orders": 0 }
  ],
  "earningsBars": [
    {
      "periodIndex": 1,
      "periodStartUtc": "2026-08-17T00:00:00Z",
      "periodEndUtc": "2026-08-24T00:00:00Z",
      "amount": 86.400,
      "subscriptionsCount": 14
    }
  ],
  "overviewKpis": {
    "registeredCustomers": 72,
    "registeredCustomersDiff": 2,
    "completedSubscriptions": 72,
    "completedSubscriptionsDiff": 1,
    "linkClicks": 1850,
    "linkClicksDiff": 5,
    "conversionRate": 3.89,
    "conversionRateDiff": 1.2
  },
  "topPackages": [
    {
      "programId": "1a2b3c4d-5e6f-7a8b-9c0d-1e2f3a4b5c6d",
      "bundleId": "e1f13579-2468-4a0b-9cde-f01234567890",
      "nameAr": "باقة خسارة الوزن دايت 28 يوم",
      "nameEn": "Weight Loss Diet 28 Days",
      "totalRevenue": 2280.000,
      "orderCount": 38,
      "totalCommission": 228.000
    },
    {
      "programId": "2a3b4c5d-6e7f-8a9b-0c1d-2e3f4a5b6c7d",
      "bundleId": "f2e24680-1357-9b1c-8ade-0123456789ab",
      "nameAr": "باقة البناء العضلي الرياضي",
      "nameEn": "Muscle Building Athletic",
      "totalRevenue": 1440.000,
      "orderCount": 24,
      "totalCommission": 144.000
    }
  ]
}
```

---

## 📱 إرشادات التنفيذ في Flutter (باستخدام `fl_chart`)

```dart
// تحويل نقاط التاريخ إلى FlSpot في Flutter
final spots = analytics.earningsChart.asMap().entries.map((entry) {
  final index = entry.key.toDouble();
  final item = entry.value;
  return FlSpot(index, item.amount);
}).toList();

LineChart(
  LineChartData(
    lineBarsData: [
      LineChartBarData(
        spots: spots,
        isCurved: true,
        color: const Color(0xFF10B981), // Emerald Green
        barWidth: 3,
        dotData: const FlDotData(show: false),
      ),
    ],
  ),
);
```

---

## ➡️ الانتقال والروابط
- للعودة إلى لوحة التحكم الرئيسية: [01-influencer-dashboard.md](./01-influencer-dashboard.md)
- للانتقال إلى المحفظة وسحب الأرباح: [03-withdrawals-and-wallet.md](./03-withdrawals-and-wallet.md)
- فهرس الشاشات وقواعد النظام: [README.md](./README.md)
