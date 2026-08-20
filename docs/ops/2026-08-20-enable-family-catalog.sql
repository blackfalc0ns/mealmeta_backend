-- Enable family catalog for Active programs/bundles (Status = 4 = ProgramStatus.Active).
-- Without this, GET /api/v1/customer/family-subscription-flow/programs returns [].
-- Safe to re-run.

UPDATE "Programs"
SET "AvailableForFamily" = TRUE
WHERE NOT "IsDeleted"
  AND "IsActive" = TRUE
  AND "Status" = 4;

UPDATE "Bundles"
SET "AvailableForFamily" = TRUE
WHERE NOT "IsDeleted"
  AND "IsActive" = TRUE
  AND "Status" = 4;

-- Verify (expect programs_family > 0 and programs_with_offers > 0 on a seeded env)
-- SELECT COUNT(*) FROM "Programs" WHERE "AvailableForFamily";
-- SELECT COUNT(*) FROM "Bundles" WHERE "AvailableForFamily";
