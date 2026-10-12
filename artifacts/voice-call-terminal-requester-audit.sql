START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011010814_AddVoiceCallTerminalRequesterAudit') THEN
    ALTER TABLE "CallSessions" ADD "EndedByDeviceSessionId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011010814_AddVoiceCallTerminalRequesterAudit') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261011010814_AddVoiceCallTerminalRequesterAudit', '9.0.3');
    END IF;
END $EF$;
COMMIT;

