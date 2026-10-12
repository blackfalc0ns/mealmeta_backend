START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011004452_AddVoiceCallDeliveryDiagnostics') THEN
    ALTER TABLE "VoiceCallEventDeliveries" ADD "ReasonCode" character varying(64);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011004452_AddVoiceCallDeliveryDiagnostics') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261011004452_AddVoiceCallDeliveryDiagnostics', '9.0.3');
    END IF;
END $EF$;
COMMIT;

