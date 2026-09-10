# دليل مطور الموبايل (Flutter) — حقل الهاتف بالكود الدولي والبحث التلقائي لإضافة أفراد العائلة

> **الجمهور:** فريق تطوير تطبيق الموبايل (Flutter / Mobile Team)  
> **الهدف:** توضيح كيفية تطبيق خانة الهاتف مع قائمة اختيار كود الدولة (Country Code Picker) والاستعلام التلقائي (Auto-Lookup) عن المستخدمين الحاليين لتعبئة بياناتهم تلقائياً قبل إضافة الفرد للعائلة.  
> **تاريخ التحديث:** سبتمبر 2026  
> **الـ Base URL:**  
> `http://maelmate.runasp.net/api/v1/customer/family-subscription-flow`

---

## 1. الفكرة وتدفق تجربة المستخدم (UX Flow)

1. مدير العائلة يفتح شاشة **«إضافة فرد للعائلة»**.
2. تظهر خانة الهاتف مقسومة إلى جزأين:
   * **محدد كود الدولة (Country Code Picker):** افتراضياً علم وكود دولة الكويت (`🇰🇼 +965`) مع إمكانية التغيير لدول أخرى (السعودية `+966`، مصر `+20`، إلخ).
   * **خانة رقم الهاتف:** لكتابة الرقم المحلي فقط (مثال: `51111111`).
3. **البحث التلقائي (Auto-Lookup):**
   * بمجرد انتهاء المستخدم من كتابة الرقم (Debounce 600ms بعد إدخال 8 أرقام أو أكثر) أو عند مغادرة الخانة (`onBlur`)، يرسل التطبيق استعلاماً للـ API:  
     `POST /api/v1/customer/family-subscription-flow/members/lookup`
4. **النتائج المحتملة:**
   * **حساب مسجل مسبقاً (`found: true`):**
     * يتم تلقائياً تعبئة الحقول:
       * **الاسم الكامل** (`displayName`)
       * **العمر** (`ageYears`) — محسوب تلقائياً من تاريخ ميلاد العميل أو سجله العائلي.
       * **النوع** (`gender`: 1 ذكر، 2 أنثى).
     * تظهر رسالة خفيفة: `✓ تم العثور على حساب مسجل: سارة أحمد`.
     * يتبقى لمدير العائلة فقط اختيار: **صلة القرابة** (`relationshipCode`) والمهنة (`occupation`).
   * **رقم جديد غير مسجل (`found: false`):**
     * تظل الحقول فارغة ليملأها المدير يدوياً (الاسم، العمر، النوع، صلة القرابة).
     * عند الحفظ النهائي، الـ Backend ينشئ للمستخدم حساب عميل تلقائياً في الخلفية.
   * **الرقم مسجل في عائلة أخرى (`409 Conflict`):**
     * تظهر رسالة خطأ باللون الأحمر تحت الخانة: `هذا الرقم ينتمي بالفعل لعائلة أخرى`.

---

## 2. مواصفات الـ APIs

### أ) الاستعلام التلقائي عن الرقم (Lookup)

* **المسار:** `POST /api/v1/customer/family-subscription-flow/members/lookup`
* **الترويسات (Headers):**
  ```http
  Authorization: Bearer <Customer_Token>
  Content-Type: application/json
  ```
* **جسم الطلب (Request Body):**
  ```json
  {
    "phone": "+96551111111"
  }
  ```

#### سيناريو 1: المستخدم موجود
* **كود الاستجابة:** `200 OK`
* **الرد (Response Body):**
  ```json
  {
    "found": true,
    "displayName": "سارة أحمد",
    "ageYears": 28,
    "gender": 2
  }
  ```
  *(ملاحظة: النوع `1` = ذكر Male، `2` = أنثى Female).*

#### سيناريو 2: المستخدم غير موجود
* **كود الاستجابة:** `200 OK`
* **الرد (Response Body):**
  ```json
  {
    "found": false,
    "displayName": null,
    "ageYears": null,
    "gender": null
  }
  ```

#### سيناريو 3: المستخدم في عائلة أخرى
* **كود الاستجابة:** `409 Conflict`
* **الرد (Response Body):**
  ```json
  {
    "code": "family.already_in_family",
    "detail": "This phone already belongs to another family."
  }
  ```

---

### ب) الحفظ النهائي للفرد (Add Member)

* **المسار:** `POST /api/v1/customer/family-subscription-flow/members`
* **جسم الطلب (Request Body):**
  ```json
  {
    "phone": "+96551111111",
    "displayName": "سارة أحمد",
    "relationshipCode": "spouse",
    "gender": 2,
    "ageYears": 28,
    "occupation": "مهندسة",
    "deliveryAddressId": null
  }
  ```
* **الرد:** كائن العائلة المحدث `FamilyGroupScreenDto`.

> [!TIP]
> في حال كان المستخدم مسجلاً مسبقاً، حتى لو لم يرسل التطبيق الاسم أو العمر، فإن السيرفر يجلبهما تلقائياً من حسابه الأصلي، ويربط الفرد بالحساب بدون إنشاء حساب مكرر.

---

## 3. قائمة أكواد الدول المدعومة (Country Codes)

| الدولة | العلم | الكود الدولي | طول الرقم المحلي الشائع |
| :--- | :---: | :---: | :---: |
| **الكويت (الافتراضي)** | 🇰🇼 | `+965` | 8 أرقام (يبدأ بـ 5, 6, 9) |
| **السعودية** | 🇸🇦 | `+966` | 9 أرقام (يبدأ بـ 5) |
| **مصر** | 🇪🇬 | `+20` | 10 أرقام (يبدأ بـ 10, 11, 12, 15) |
| **الإمارات** | 🇦🇪 | `+971` | 9 أرقام (يبدأ بـ 5) |
| **قطر** | 🇶🇦 | `+974` | 8 أرقام |
| **البحرين** | 🇧🇭 | `+973` | 8 أرقام |
| **عُمان** | 🇴🇲 | `+968` | 8 أرقام |

---

## 4. كود Flutter الكامل (جاهز للاستخدام والنسخ)

### أ) الموديل (Model DTO)

```dart
class FamilyMemberLookupResult {
  final bool found;
  final String? displayName;
  final int? ageYears;
  final int? gender; // 1 = Male, 2 = Female

  FamilyMemberLookupResult({
    required this.found,
    this.displayName,
    this.ageYears,
    this.gender,
  });

  factory FamilyMemberLookupResult.fromJson(Map<String, dynamic> json) {
    return FamilyMemberLookupResult(
      found: json['found'] ?? false,
      displayName: json['displayName'],
      ageYears: json['ageYears'],
      gender: json['gender'],
    );
  }
}
```

---

### ب) ويدجت إدخال الهاتف مع محدد الدولة والبحث التلقائي (Widget)

```dart
import 'dart:async';
import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

class PhoneInputWithLookupField extends StatefulWidget {
  final String apiBaseUrl;
  final String authToken;
  final Function(FamilyMemberLookupResult result, String fullPhoneNumber) onLookupCompleted;
  final Function(String? error) onErrorChanged;

  const PhoneInputWithLookupField({
    super.key,
    required this.apiBaseUrl,
    required this.authToken,
    required this.onLookupCompleted,
    required this.onErrorChanged,
  });

  @override
  State<PhoneInputWithLookupField> createState() => _PhoneInputWithLookupFieldState();
}

class _PhoneInputWithLookupFieldState extends State<PhoneInputWithLookupField> {
  String _selectedCountryCode = '+965';
  final TextEditingController _phoneController = TextEditingController();
  final FocusNode _focusNode = FocusNode();
  Timer? _debounceTimer;
  bool _isLoading = false;
  String? _inlineError;
  String? _lastSearchedPhone;

  final List<Map<String, String>> _countryList = const [
    {'code': '+965', 'flag': '🇰🇼', 'name': 'الكويت'},
    {'code': '+966', 'flag': '🇸🇦', 'name': 'السعودية'},
    {'code': '+20',  'flag': '🇪🇬', 'name': 'مصر'},
    {'code': '+971', 'flag': '🇦🇪', 'name': 'الإمارات'},
    {'code': '+974', 'flag': '🇶🇦', 'name': 'قطر'},
    {'code': '+973', 'flag': '🇧🇭', 'name': 'البحرين'},
    {'code': '+968', 'flag': '🇴🇲', 'name': 'عُمان'},
  ];

  @override
  void initState() {
    super.initState();
    // تفعيل البحث عند مغادرة الحقل (On Blur)
    _focusNode.addListener(() {
      if (!_focusNode.hasFocus) {
        _triggerLookup();
      }
    });
  }

  @override
  void dispose() {
    _debounceTimer?.cancel();
    _phoneController.dispose();
    _focusNode.dispose();
    super.dispose();
  }

  void _onTextChanged(String value) {
    _debounceTimer?.cancel();
    final digits = value.replaceAll(RegExp(r'\D'), '');
    // بحث تلقائي بعد توقف الكتابة إذا كان الرقم 8 خانات فأكثر
    if (digits.length >= 8) {
      _debounceTimer = Timer(const Duration(milliseconds: 600), _triggerLookup);
    }
  }

  Future<void> _triggerLookup() async {
    final rawNumber = _phoneController.text.replaceAll(RegExp(r'\D'), '');
    if (rawNumber.isEmpty) return;

    final fullPhone = '$_selectedCountryCode$rawNumber';
    if (fullPhone == _lastSearchedPhone) return;

    setState(() {
      _isLoading = true;
      _inlineError = null;
    });
    widget.onErrorChanged(null);

    try {
      final dio = Dio(BaseOptions(baseUrl: widget.apiBaseUrl));
      final response = await dio.post(
        '/api/v1/customer/family-subscription-flow/members/lookup',
        data: {'phone': fullPhone},
        options: Options(
          headers: {'Authorization': 'Bearer ${widget.authToken}'},
        ),
      );

      _lastSearchedPhone = fullPhone;
      final result = FamilyMemberLookupResult.fromJson(response.data);
      widget.onLookupCompleted(result, fullPhone);
    } on DioException catch (e) {
      String msg = 'تعذر التحقق من الرقم، يرجى المحاولة لاحقاً';
      if (e.response?.statusCode == 409) {
        msg = 'هذا الرقم مسجل بالفعل في عائلة أخرى';
      }
      setState(() => _inlineError = msg);
      widget.onErrorChanged(msg);
    } finally {
      if (mounted) setState(() => _isLoading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const Text(
          'رقم الموبايل',
          style: TextStyle(fontSize: 14, fontWeight: FontWeight.bold),
        ),
        const SizedBox(height: 6),
        Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            // Dropdown قائمة الأكواد الدولية
            Container(
              height: 56,
              padding: const EdgeInsets.symmetric(horizontal: 10),
              decoration: BoxDecoration(
                border: Border.all(color: Colors.grey.shade400),
                borderRadius: BorderRadius.circular(10),
                color: Colors.grey.shade50,
              ),
              child: DropdownButtonHideUnderline(
                child: DropdownButton<String>(
                  value: _selectedCountryCode,
                  icon: const Icon(Icons.arrow_drop_down),
                  items: _countryList.map((country) {
                    return DropdownMenuItem<String>(
                      value: country['code'],
                      child: Row(
                        children: [
                          Text(country['flag']!, style: const TextStyle(fontSize: 18)),
                          const SizedBox(width: 6),
                          Text(
                            country['code']!,
                            style: const TextStyle(fontWeight: FontWeight.w600),
                          ),
                        ],
                      ),
                    );
                  }).toList(),
                  onChanged: (val) {
                    if (val != null && val != _selectedCountryCode) {
                      setState(() => _selectedCountryCode = val);
                      _triggerLookup();
                    }
                  },
                ),
              ),
            ),
            const SizedBox(width: 8),
            // حقل كتابة باقي الأرقام
            Expanded(
              child: TextFormField(
                controller: _phoneController,
                focusNode: _focusNode,
                keyboardType: TextInputType.phone,
                decoration: InputDecoration(
                  hintText: '51111111',
                  contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 16),
                  border: OutlineInputBorder(borderRadius: BorderRadius.circular(10)),
                  suffixIcon: _isLoading
                      ? const Padding(
                          padding: EdgeInsets.all(12),
                          child: SizedBox(
                            width: 18,
                            height: 18,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          ),
                        )
                      : null,
                ),
                onChanged: _onTextChanged,
              ),
            ),
          ],
        ),
        if (_inlineError != null)
          Padding(
            padding: const EdgeInsets.only(top: 6, right: 4),
            child: Text(
              _inlineError!,
              style: const TextStyle(color: Colors.red, fontSize: 12),
            ),
          ),
      ],
    );
  }
}
```

---

### جـ) كود شاشة إضافة الفرد ودمج الـ Auto-Fill

```dart
class AddFamilyMemberScreen extends StatefulWidget {
  final String apiBaseUrl;
  final String authToken;

  const AddFamilyMemberScreen({
    super.key,
    required this.apiBaseUrl,
    required this.authToken,
  });

  @override
  State<AddFamilyMemberScreen> createState() => _AddFamilyMemberScreenState();
}

class _AddFamilyMemberScreenState extends State<AddFamilyMemberScreen> {
  final _nameController = TextEditingController();
  final _ageController = TextEditingController();
  final _occupationController = TextEditingController();
  int? _selectedGender; // 1 = Male, 2 = Female
  String? _selectedRelationship = 'spouse';
  String? _resolvedPhone;
  bool _isExistingUser = false;

  void _handleLookupCompleted(FamilyMemberLookupResult result, String fullPhone) {
    setState(() {
      _resolvedPhone = fullPhone;
      _isExistingUser = result.found;

      if (result.found) {
        // تعبئة البيانات تلقائياً
        if (result.displayName != null) {
          _nameController.text = result.displayName!;
        }
        if (result.ageYears != null) {
          _ageController.text = result.ageYears.toString();
        }
        if (result.gender != null) {
          _selectedGender = result.gender;
        }

        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            backgroundColor: Colors.green.shade700,
            content: Text('✓ تم العثور على حساب مسجل: ${result.displayName ?? fullPhone}'),
          ),
        );
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('إضافة فرد للعائلة')),
      body: SingleChildScrollView(
        padding: const EdgeInsets.all(16),
        child: Column(
          children: [
            // 1. حقل الهاتف بالكود الدولي والبحث التلقائي
            PhoneInputWithLookupField(
              apiBaseUrl: widget.apiBaseUrl,
              authToken: widget.authToken,
              onLookupCompleted: _handleLookupCompleted,
              onErrorChanged: (err) {},
            ),
            const SizedBox(height: 16),

            // 2. حقل الاسم الكامل
            TextFormField(
              controller: _nameController,
              decoration: const InputDecoration(
                labelText: 'الاسم الكامل',
                border: OutlineInputBorder(),
              ),
            ),
            const SizedBox(height: 16),

            // 3. حقل العمر بالسنوات
            TextFormField(
              controller: _ageController,
              keyboardType: TextInputType.number,
              decoration: const InputDecoration(
                labelText: 'العمر بالسنوات',
                border: OutlineInputBorder(),
              ),
            ),
            const SizedBox(height: 16),

            // 4. اختيار النوع (ذكر / أنثى)
            DropdownButtonFormField<int>(
              value: _selectedGender,
              decoration: const InputDecoration(
                labelText: 'النوع',
                border: OutlineInputBorder(),
              ),
              items: const [
                DropdownMenuItem(value: 1, child: Text('ذكر')),
                DropdownMenuItem(value: 2, child: Text('أنثى')),
              ],
              onChanged: (val) => setState(() => _selectedGender = val),
            ),
            const SizedBox(height: 16),

            // 5. صلة القرابة
            DropdownButtonFormField<String>(
              value: _selectedRelationship,
              decoration: const InputDecoration(
                labelText: 'صلة القرابة',
                border: OutlineInputBorder(),
              ),
              items: const [
                DropdownMenuItem(value: 'spouse', child: Text('زوج / زوجة')),
                DropdownMenuItem(value: 'child', child: Text('ابن / ابنة')),
                DropdownMenuItem(value: 'father', child: Text('أب')),
                DropdownMenuItem(value: 'mother', child: Text('أم')),
                DropdownMenuItem(value: 'sibling', child: Text('أخ / أخت')),
                DropdownMenuItem(value: 'friend', child: Text('صديق / صديقة')),
                DropdownMenuItem(value: 'other', child: Text('أخرى')),
              ],
              onChanged: (val) => setState(() => _selectedRelationship = val),
            ),
            const SizedBox(height: 24),

            // زر الحفظ
            ElevatedButton(
              style: ElevatedButton.styleFrom(
                minimumSize: const Size.fromHeight(50),
              ),
              onPressed: () {
                // إرسال POST /api/v1/customer/family-subscription-flow/members
              },
              child: const Text('إضافة الفرد للعائلة', style: TextStyle(fontSize: 16)),
            ),
          ],
        ),
      ),
    );
  }
}
```

---

## 5. ملاحظات ختامية للمطور

1. **لا ترسل كود الدولة مفصولاً:** الـ API يتوقع دائماً حقلاً واحداً اسمه `phone` بالصيغة الدولية المدمجة (مثال: `"+96551111111"`).
2. **الـ Trim والتنظيف:** السيرفر يقوم تلقائياً بمسح المسافات وتحويل `00` إلى `+`، ولكن من الأفضل دائماً إرسالها نظيفة من تطبيق الموبايل.
3. **تجنب تكرار الاستدعاء:** تم استخدام `Debounce Timer` لمنع إرسال طلبات زائدة أثناء سرعة الكتابة، واستدعاء الاستعلام فقط بعد اكتمال 8 أرقام أو عند مغادرة الحقل.
