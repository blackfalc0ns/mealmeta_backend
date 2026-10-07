# Payzah payment gateway migration
Approved scope: replace the active MyFatoorah gateway with a new Payzah integration across backend checkout, settings, callbacks, refunds and admin settings. Preserve applied migrations and historical payment identifiers/providers. No deployments or live financial requests.

Design: use a provider-neutral payment interface and explicit creation receipt. Persist a unique Payzah attempt (track ID, creation payment ID, reference, expected amount/currency) before verification. Callback input is untrusted; query Payzah server status with persisted identifiers and require exact track/reference binding. Credentials are encrypted separately. Legacy unresolved financial operations require review and never enter Payzah. Unsupported provider operations fail explicitly.

Acceptance: backend builds; payment tests cover authorization encoding, explicit receipt, unbound status rejection, callback binding and legacy refund protection. Admin uses new Payzah settings. No mock business data in runtime, old active provider code and credentials removed, applied history retained.
