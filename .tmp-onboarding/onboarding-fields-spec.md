# Restaurant onboarding fields from MealMate_Restaurant_Onboarding_Template_v3.0 sheet «بيانات المطعم»

Source title: بيانات المطعم والمسؤول والحساب البنكي

## Persist + reviewable fields (map to RestaurantRegistration + RegistrationFieldKeys)

### Restaurant / company
| system_key | AR label | required | suggested property |
|---|---|---|---|
| restaurant_code | رمز المطعم في MealMate | no | RestaurantCode |
| official_company_name_ar | اسم الشركة الرسمي بالعربية | yes | LegalNameAr (exists) |
| official_company_name_en | اسم الشركة الرسمي بالإنجليزية | yes | LegalNameEn (exists) |
| restaurant_brand_name_ar | اسم المطعم التجاري بالعربية | yes | TradeNameAr (exists) |
| restaurant_brand_name_en | اسم المطعم التجاري بالإنجليزية | yes | TradeNameEn (exists) |
| legal_entity_type | نوع الكيان القانوني (شركة/مؤسسة) | yes | LegalEntityType |
| company_civil_id | الرقم المدني للمنشأة | yes | CompanyCivilId |
| commercial_registration_number | رقم السجل التجاري | yes | CommercialRegistrationNumber (exists) |
| commercial_registration_expiry | تاريخ انتهاء السجل التجاري | yes | CommercialRegistrationExpiry |
| commercial_license_number | رقم الرخصة التجارية | yes | CommercialLicenseNumber |
| commercial_license_expiry | تاريخ انتهاء الرخصة التجارية | yes | CommercialLicenseExpiry |
| food_license_number | رقم ترخيص المنشأة الغذائية | yes | FoodLicenseNumber |
| food_license_expiry | تاريخ انتهاء ترخيص المنشأة الغذائية | yes | FoodLicenseExpiry |
| headquarters_address_ar | العنوان القانوني الرئيسي | yes | Address (exists) |
| restaurant_email | البريد الإلكتروني الرسمي | yes | ContactEmail (exists) |
| restaurant_phone | هاتف المطعم الرسمي | yes | ContactPhone (exists) |
| website | الموقع الإلكتروني | no | Website |
| instagram_handle | حساب Instagram | no | InstagramHandle |

### Responsible persons
| system_key | AR label | required | suggested property |
|---|---|---|---|
| primary_contact_name | اسم مسؤول البيانات | yes | PrimaryContactName |
| primary_contact_title | المسمى الوظيفي لمسؤول البيانات | yes | PrimaryContactTitle |
| primary_contact_phone | هاتف مسؤول البيانات | yes | PrimaryContactPhone |
| primary_contact_email | بريد مسؤول البيانات | yes | PrimaryContactEmail |
| authorized_signatory_name | اسم المفوض بالتوقيع | yes | OwnerName / AuthorizedSignatoryName |
| authorized_signatory_civil_id | الرقم المدني للمفوض | yes | AuthorizedSignatoryCivilId |
| authorized_signatory_title | صفة المفوض بالتوقيع | yes | AuthorizedSignatoryTitle |

### Bank
| system_key | AR label | required | suggested property |
|---|---|---|---|
| bank_name | اسم البنك | yes | BankName |
| beneficiary_name | اسم المستفيد البنكي | yes | BankBeneficiaryName |
| iban | رقم IBAN (KW + 30 chars no spaces) | yes | Iban |
| account_number | رقم الحساب البنكي | yes | BankAccountNumber |
| swift_code | رمز SWIFT/BIC | no | SwiftCode |
| settlement_currency | عملة التسوية | yes | SettlementCurrency (default KWD) |

### Confirmations (optional for v1 review, store if easy)
| system_key | notes |
|---|---|
| delivery_by_restaurant_confirmed | yes/نعم |
| data_responsibility_confirmed | yes/نعم |
| submission_date | date |
| restaurant_notes | optional notes |

## Acceptance
1. Backend stores all required restaurant/contact/bank fields on RestaurantRegistration.
2. Create/Update registration API accepts and returns them.
3. RegistrationFieldKeys includes them for admin field-by-field review (sections: identity, contact, bank).
4. Restaurant onboarding UI collects AR/EN names separately, full responsible-person block, full bank block; persists via API (no longer UI-only).
5. Admin pending detail shows and reviews the new fields.
6. IBAN validation: starts with KW, length 30 without spaces.
7. EF migration added.
8. Existing flows keep working for drafts that lack new optional fields; required fields enforced on submit.
