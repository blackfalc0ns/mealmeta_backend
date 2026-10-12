START TRANSACTION;
DROP TABLE "MyFatoorahGatewayConfigs";

UPDATE "PaymentGatewayConfigs" SET "IsActive" = FALSE WHERE LOWER("Code") = 'myfatoorah' OR LOWER("Name") = 'myfatoorah';

UPDATE "RefundOperations" SET "State" = 5, "Detail" = 'Historical provider operation requires merchant review; no Payzah request was sent.', "Version" = "Version" + 1 WHERE LOWER("PaymentProvider") = 'myfatoorah' AND "State" IN (0, 1, 2);

CREATE TABLE "PaymentGatewayAttempts" (
    "Id" uuid NOT NULL,
    "Provider" text NOT NULL,
    "TrackId" character varying(100) NOT NULL,
    "PaymentId" character varying(255) NOT NULL,
    "CustomerReference" character varying(255) NOT NULL,
    "ExpectedAmount" numeric(18,3) NOT NULL,
    "ExpectedCurrency" text NOT NULL,
    "IsSandbox" boolean NOT NULL,
    "ProviderReference" text,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "CreatedByUserId" uuid,
    "ModifiedAtUtc" timestamp with time zone,
    "ModifiedByUserId" uuid,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    "DeletedByUserId" uuid,
    CONSTRAINT "PK_PaymentGatewayAttempts" PRIMARY KEY ("Id")
);

CREATE TABLE "PayzahGatewayConfigs" (
    "Id" uuid NOT NULL,
    "GatewayName" text NOT NULL,
    "IsSandbox" boolean NOT NULL,
    "BaseUrl" text NOT NULL,
    "EncryptedPrivateKey" text NOT NULL,
    "DefaultCurrency" text NOT NULL,
    "CountryCode" text NOT NULL,
    "MaskedToken" text NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "CreatedByUserId" uuid,
    "ModifiedAtUtc" timestamp with time zone,
    "ModifiedByUserId" uuid,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    "DeletedByUserId" uuid,
    CONSTRAINT "PK_PayzahGatewayConfigs" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX "IX_PaymentGatewayAttempts_Provider_IsSandbox_PaymentId" ON "PaymentGatewayAttempts" ("Provider", "IsSandbox", "PaymentId") WHERE "PaymentId" <> '';

CREATE UNIQUE INDEX "IX_PaymentGatewayAttempts_TrackId" ON "PaymentGatewayAttempts" ("TrackId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261006200145_ReplaceActiveGatewayWithPayzah', '9.0.3');

COMMIT;

