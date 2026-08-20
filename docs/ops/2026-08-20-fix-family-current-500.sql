-- Hotfix for maelmate.runasp.net family 500 on GET .../family-subscription-flow/current
-- Cause: deployed code expects columns that Production never migrated (Seed/Migrate skipped in Production).

-- 1) Family package selection
ALTER TABLE "FamilyGroups"
    ADD COLUMN IF NOT EXISTS "SelectedFamilyPackageId" uuid NULL;

CREATE INDEX IF NOT EXISTS "IX_FamilyGroups_SelectedFamilyPackageId"
    ON "FamilyGroups" ("SelectedFamilyPackageId");

-- 2) Family catalog eligibility flags
ALTER TABLE "Programs"
    ADD COLUMN IF NOT EXISTS "AvailableForFamily" boolean NOT NULL DEFAULT false;

ALTER TABLE "Bundles"
    ADD COLUMN IF NOT EXISTS "AvailableForFamily" boolean NOT NULL DEFAULT false;

CREATE INDEX IF NOT EXISTS "IX_Programs_AvailableForFamily"
    ON "Programs" ("AvailableForFamily");

CREATE INDEX IF NOT EXISTS "IX_Bundles_AvailableForFamily"
    ON "Bundles" ("AvailableForFamily");

-- 3) Mark EF migrations applied (only if not already present)
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260820110000_AddAvailableForFamilyToProgramAndBundle', '9.0.0'
WHERE NOT EXISTS (
    SELECT 1 FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260820110000_AddAvailableForFamilyToProgramAndBundle'
);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260820140000_AddSelectedFamilyPackageIdToFamilyGroup', '9.0.0'
WHERE NOT EXISTS (
    SELECT 1 FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260820140000_AddSelectedFamilyPackageIdToFamilyGroup'
);
