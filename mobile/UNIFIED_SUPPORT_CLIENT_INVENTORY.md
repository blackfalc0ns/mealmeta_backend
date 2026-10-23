# Unified support mobile client inventory

Checked in the MealMate repository on 2026-10-23. No authoritative production mobile app source was found in this checkout. The repository contains API/mobile handoff documents, but no mobile app package manifest (`pubspec.yaml`, Android application module, or mobile JavaScript package) was identified. Scratch/mobile-call-fix is a scratch checkout and is not a supported release target. Consequently this inventory does not identify a production branch, commit, build target, or deployed app version.

The backend owns the versioned API contract at [UNIFIED_SUPPORT_API_CONTRACT.md](UNIFIED_SUPPORT_API_CONTRACT.md). Customer, driver, restaurant, and delivery-manager client teams should integrate from their authoritative app repository after its owner confirms the source, API base URL, authentication refresh behavior, and minimum released version. Do not copy implementation into scratch or claim a mobile release from this backend-only change.

The existing driver support handover contract remains the source for custody transfers and acknowledgement/receipt operations. The new operational issue projection invokes the existing server handlers; it does not replace the mobile driver's report-issue flow or handover screens.
