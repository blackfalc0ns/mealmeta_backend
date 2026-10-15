START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261015120000_AddFamilyCredentialOperations') THEN
    CREATE TABLE "FamilyCredentialOperations" (
        "Id" uuid NOT NULL,
        "FamilyMemberId" uuid NOT NULL,
        "ActorUserId" uuid NOT NULL,
        "OperationKeyHash" character varying(64) NOT NULL,
        "CompletedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "ModifiedAtUtc" timestamp with time zone,
        "CreatedByUserId" uuid,
        "ModifiedByUserId" uuid,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        "DeletedByUserId" uuid,
        CONSTRAINT "PK_FamilyCredentialOperations" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261015120000_AddFamilyCredentialOperations') THEN
    CREATE INDEX "IX_FamilyCredentialOperations_ActorUserId" ON "FamilyCredentialOperations" ("ActorUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261015120000_AddFamilyCredentialOperations') THEN
    CREATE UNIQUE INDEX "IX_FamilyCredentialOperations_FamilyMemberId_OperationKeyHash" ON "FamilyCredentialOperations" ("FamilyMemberId", "OperationKeyHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261015120000_AddFamilyCredentialOperations') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261015120000_AddFamilyCredentialOperations', '9.0.3');
    END IF;
END $EF$;
COMMIT;

