# Screen 04.09: أداء السائقين ومقارنة الكفاءة (Screen 10.09 - Driver Performance)

## 🎯 الغرض من الشاشة ونظرة عامة (Overview & Purpose)
تُمثّل هذه الشاشة الواجهة التحليلية والتشغيلية الشاملة لمسؤول التوزيع (`Dispatcher`) لتقييم كفاءة أسطول التوصيل التابع للمطعم، وتفتح عند:
1. النقر على تبويب أو أيقونة "أداء السائقين" في لوحة تحكم الحركة (`Screen 04.01`).
2. الانتقال من القائمة الجانبية أو السفلية للمرسل (`Dispatcher Navigation`).

تتيح الشاشة لمسؤول التوصيل:
- متابعة الكفاءة والإنتاجية خلال فترات زمنية مرنة (اليوم، آخر 7 أيام، هذا الأسبوع، هذا الشهر، أو فترة مخصصة).
- الاطلاع على **5 مؤشرات أداء رئيسية (KPIs)** تبرز حجم العمل، نسب النجاح، ومعدلات التأخير والتعثر.
- مراجعة **جدول أداء السائقين التفصيلي** مع دعم الترتيب والتنقل المباشر للملف التشغيلي للسائق (`Screen 10.07`).
- تحليل **توزيع الأداء العام (Performance Distribution)** عبر رسم بياني حلقي (Donut Chart) يقسم الطلبات إلى (في الوقت، متأخر، فاشل، ملغي).
- تكريم وتحفيز الكوادر عبر **منصة التتويج لأفضل 3 سائقين تقييماً (Top 3 Podium)**.
- التبديل إلى تبويب **مقارنة السائقين (Driver Comparison)** لمقارنة كفاءة سائقين متعددين جنباً إلى جنب.

---

## 🎨 التحليل البصري والتخطيطي (UI/UX Breakdown & Components)

```
┌────────────────────────────────────────────────────────┐
│  [<]                   أداء السائقين         [آخر 7 أيام 📅 ⌵] │
├────────────────────────────────────────────────────────┤
│     [ مقارنة السائقين ]       │   [ نظرة عامة (نشط) ]   │
├────────────────────────────────────────────────────────┤
│  ┌──────────┬──────────┬──────────┬──────────┬──────────┐ │
│  │ 🚫 فشل   │ ⭐ تقييم │ 🕒 تأخير │ ✅ توصيل │ 📦 إجمالي│ │
│  │   4      │   4.6    │   14     │   112    │   128    │ │
│  │  3.1%    │  من 5    │  دقيقة   │  87.5%   │   بوكس   │ │
│  └──────────┴──────────┴──────────┴──────────┴──────────┘ │
├────────────────────────────────────────────────────────┤
│  أداء السائقين (Driver Performance Table)               │
│  السائق            تم التوصيل  متوسط التأخير  فشل    تقييم   │
│ ───────────────────────────────────────────────────────│
│  [🟢] أحمد السعيد   32 (90%)      8 د (🟢)   0 (0%)  ⭐ 4.9 [<]│
│       DR-1025                                          │
│  [🟠] محمد العنزي   28 (85%)     12 د (🟠)   1 (3%)  ⭐ 4.6 [<]│
│       DR-1032                                          │
│  [🟢] يوسف خالد     24 (86%)     15 د (🟠)   1 (3.5%)⭐ 4.5 [<]│
│       DR-1018                                          │
│  [🟠] فهد المطيري   18 (78%)     22 د (🔴)   2 (8.7%)⭐ 4.1 [<]│
│       DR-1041                                          │
│  [🔴] عبدالله الشهري10 (62%)     35 د (🔴)   3 (18.7%)⭐ 3.2[<]│
│       DR-1007                                          │
├────────────────────────────────────────────────────────┤
│  توزيع الأداء (Performance Distribution)                │
│  ┌──────────────────┬────────────────────────────────┐ │
│  │     🍩           │  🟢 تم في الوقت  (79)     62%  │ │
│  │   ( 128 )        │  🟠 متأخر       (32)     25%  │ │
│  │    بوكس          │  🔴 فشل التسليم (11)      9%  │ │
│  │                  │  ⚪ ملغي         (6)      4%  │ │
│  └──────────────────┴────────────────────────────────┘ │
├────────────────────────────────────────────────────────┤
│  أعلى السائقين تقييماً (Top 3 Podium)                   │
│                                                        │
│           ┌──────────┐                                 │
│           │   🥇 1   │                                 │
│  ┌──────┐ │أحمد السعيد│ ┌──────┐                        │
│  │ 🥉 3 │ │ ⭐ 4.6   │ │ 🥈 2 │                        │
│  │يوسف خ│ └──────────┘ │محمد ع│                        │
│  │⭐ 4.5│  [بنفسجي بارز]│⭐ 4.6│                        │
│  └──────┘              └──────┘                        │
└────────────────────────────────────────────────────────┘
```

---

## 🔌 عقود التكامل والخادم (API Contracts & Modular Endpoints)

### 1. نظرة عامة على الأداء (Tab 1: Overview)
- **الرابط:** `GET /api/v1/dispatcher/performance/overview`
- **المعاملات (Query Parameters):**
  - `period`: اختياري (`Last7Days` الافتراضي، `Today`، `ThisWeek`، `ThisMonth`، `Last30Days`، أو `Custom`).
  - `restaurantId`: اختياري (يتم استنتاجه تلقائياً من توكن المستخدم).
  - `fromDate` & `toDate`: اختياري في حال اختيار `Custom`.
- **الاستجابة (200 OK):**
```json
{
  "period": "Last7Days",
  "periodText": "آخر 7 أيام",
  "dateRangeText": "15 سبتمبر - 22 سبتمبر 2026",
  "kpis": {
    "totalBoxes": 128,
    "deliveredCount": 112,
    "deliveredPercentage": 87.5,
    "avgDelayMinutes": 14,
    "overallRating": 4.6,
    "failedCount": 4,
    "failedPercentage": 3.1
  },
  "distribution": {
    "totalCount": 128,
    "segments": [
      {
        "key": "OnTime",
        "title": "تم في الوقت",
        "count": 79,
        "percentage": 62.0,
        "colorHex": "#10B981"
      },
      {
        "key": "Delayed",
        "title": "متأخر",
        "count": 32,
        "percentage": 25.0,
        "colorHex": "#F59E0B"
      },
      {
        "key": "Failed",
        "title": "فشل التسليم",
        "count": 11,
        "percentage": 9.0,
        "colorHex": "#EF4444"
      },
      {
        "key": "Cancelled",
        "title": "ملغي",
        "count": 6,
        "percentage": 4.0,
        "colorHex": "#9CA3AF"
      }
    ]
  },
  "topDrivers": [
    {
      "rank": 1,
      "driverId": "11111111-1111-1111-1111-111111111111",
      "driverCode": "DR-1025",
      "fullName": "أحمد السعيد",
      "rating": 4.6,
      "avatarUrl": "https://cdn.mealmate.app/avatars/11111111.jpg",
      "isHighlighted": true
    },
    {
      "rank": 2,
      "driverId": "22222222-2222-2222-2222-222222222222",
      "driverCode": "DR-1032",
      "fullName": "محمد العنزي",
      "rating": 4.6,
      "avatarUrl": "https://cdn.mealmate.app/avatars/22222222.jpg",
      "isHighlighted": false
    },
    {
      "rank": 3,
      "driverId": "33333333-3333-3333-3333-333333333333",
      "driverCode": "DR-1018",
      "fullName": "يوسف خالد",
      "rating": 4.5,
      "avatarUrl": "https://cdn.mealmate.app/avatars/33333333.jpg",
      "isHighlighted": false
    }
  ],
  "driversTable": [
    {
      "driverId": "11111111-1111-1111-1111-111111111111",
      "driverCode": "DR-1025",
      "fullName": "أحمد السعيد",
      "avatarUrl": "https://cdn.mealmate.app/avatars/11111111.jpg",
      "status": "Available",
      "statusDotColor": "Green",
      "deliveredCount": 32,
      "deliveredRate": 90.0,
      "deliveredRateText": "90%",
      "avgDelayMinutes": 8,
      "avgDelayText": "8 د",
      "avgDelayColor": "Green",
      "failedCount": 0,
      "failedRate": 0.0,
      "failedRateText": "0%",
      "rating": 4.9
    },
    {
      "driverId": "22222222-2222-2222-2222-222222222222",
      "driverCode": "DR-1032",
      "fullName": "محمد العنزي",
      "avatarUrl": "https://cdn.mealmate.app/avatars/22222222.jpg",
      "status": "Busy",
      "statusDotColor": "Orange",
      "deliveredCount": 28,
      "deliveredRate": 85.0,
      "deliveredRateText": "85%",
      "avgDelayMinutes": 12,
      "avgDelayText": "12 د",
      "avgDelayColor": "Orange",
      "failedCount": 1,
      "failedRate": 3.0,
      "failedRateText": "3%",
      "rating": 4.6
    }
  ]
}
```

---

### 2. مقارنة السائقين جنباً إلى جنب (Tab 2: Comparison)
- **الرابط:** `GET /api/v1/dispatcher/performance/comparison`
- **المعاملات:**
  - `period`: اختياري.
  - `driverIds`: مصفوفة معرّفات سائقين للمقارنة المحددة (أو تركها فارغة لمقارنة جميع السائقين النشطين).
- **الاستجابة (200 OK):**
```json
{
  "period": "Last7Days",
  "periodText": "آخر 7 أيام",
  "drivers": [
    {
      "driverId": "11111111-1111-1111-1111-111111111111",
      "driverCode": "DR-1025",
      "fullName": "أحمد السعيد",
      "avatarUrl": "https://cdn.mealmate.app/avatars/11111111.jpg",
      "totalAssigned": 35,
      "deliveredCount": 32,
      "onTimePercentage": 93.8,
      "avgDelayMinutes": 8,
      "rating": 4.9,
      "failedCount": 0,
      "totalDistanceKm": 142.5
    },
    {
      "driverId": "22222222-2222-2222-2222-222222222222",
      "driverCode": "DR-1032",
      "fullName": "محمد العنزي",
      "avatarUrl": "https://cdn.mealmate.app/avatars/22222222.jpg",
      "totalAssigned": 33,
      "deliveredCount": 28,
      "onTimePercentage": 82.1,
      "avgDelayMinutes": 12,
      "rating": 4.6,
      "failedCount": 1,
      "totalDistanceKm": 118.0
    }
  ]
}
```

---

## 🎨 دلالات الألوان والحالات البصرية (Color Tokens & Status Codes)

| العنصر | القيمة | اللون البصري | الاستخدام والدلالة |
|---|---|---|---|
| **إجمالي البوكسات** | Total Boxes | `#6366F1` (Indigo / Purple) | خلفية الكارت الأول البارز |
| **تم في الوقت / إنجاز ممتاز** | OnTime / Green | `#10B981` (Emerald Green) | تأخير <= 10 دقائق، نسبة تسليم عالية |
| **تأخير متوسط / قيد العمل** | Delayed / Orange | `#F59E0B` (Amber Orange) | تأخير بين 11-20 دقيقة |
| **فشل تسليم / تأخير حرج** | Failed / Red | `#EF4444` (Rose Red) | تعثر التسليم، تأخير > 20 دقيقة |
| **طلبات ملغاة** | Cancelled / Grey | `#9CA3AF` (Cool Grey) | طلبات ملغاة من المطعم أو العميل |
| **منصة التتويج المركز الأول** | Rank 1 | `#7C3AED` (Royal Violet) | الكارت الأوسط المرتفع ذو الشارة الذهبية 🥇 |

---

## 🔄 مسار التنقل والتفاعل (Interactive Flow)

1. **زر الرجوع `[<]`**: يعود إلى لوحة التحكم الرئيسية (`Screen 04.01`).
2. **منتقي الفترة الزمنية `[آخر 7 أيام 📅 ⌵]`**: يفتح القائمة المنسدلة لاختيار الفترة؛ وعند التغيير يتم إعادة استدعاء `GET /performance/overview?period=...` فوراً.
3. **تبويبات العرض**:
   - النقر على `نظرة عامة`: يعرض المؤشرات، جدول الأداء، الدونات، ومنصة التتويج.
   - النقر على `مقارنة السائقين`: يستدعي `GET /performance/comparison` لعرض أعمدة المقارنة المباشرة.
4. **النقر على أي صف سائق `[<]`**: يفتح مباشرة شاشة **تفاصيل السائق ومساره اللحظي (`Screen 10.07 / 04.07`)** عبر `driverId`.
