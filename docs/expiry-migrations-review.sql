START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123123556_AddExpiryProcessingWorkflow') THEN
    ALTER TABLE "ExpiryProcessings" ALTER COLUMN "ProcessedByEmployeeId" DROP NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123123556_AddExpiryProcessingWorkflow') THEN
    ALTER TABLE "ExpiryProcessings" ALTER COLUMN "CollectionDeadline" DROP NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123123556_AddExpiryProcessingWorkflow') THEN
    ALTER TABLE "ExpiryProcessings" ADD "Evidence" character varying(1000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123123556_AddExpiryProcessingWorkflow') THEN
    ALTER TABLE "ExpiryProcessings" ADD "Note" character varying(1000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123123556_AddExpiryProcessingWorkflow') THEN
    ALTER TABLE "ExpiryProcessings" ADD "RecipientOrganization" character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123123556_AddExpiryProcessingWorkflow') THEN
    ALTER TABLE "ExpiryProcessings" ADD "UnclaimedApprovedAt" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123123556_AddExpiryProcessingWorkflow') THEN
    ALTER TABLE "ExpiryProcessings" ADD "UnclaimedApprovedByEmployeeId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123123556_AddExpiryProcessingWorkflow') THEN
    ALTER TABLE "ExpiryProcessings" ADD "UnclaimedBasis" character varying(1000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123123556_AddExpiryProcessingWorkflow') THEN
    INSERT INTO "Permissions" ("Id", "Code", "Description", "Name")
    VALUES ('30000000-0000-0000-0000-000000000032', 'expiry.read', 'Xem phiếu xử lý hàng ký gửi hết hạn.', 'Xem hàng hết hạn');
    INSERT INTO "Permissions" ("Id", "Code", "Description", "Name")
    VALUES ('30000000-0000-0000-0000-000000000033', 'expiry.return', 'Xác nhận bàn giao hàng cho người ký gửi.', 'Giao trả hàng hết hạn');
    INSERT INTO "Permissions" ("Id", "Code", "Description", "Name")
    VALUES ('30000000-0000-0000-0000-000000000034', 'expiry.donate', 'Xác nhận bàn giao cho tổ chức nhận.', 'Bàn giao hàng quyên góp');
    INSERT INTO "Permissions" ("Id", "Code", "Description", "Name")
    VALUES ('30000000-0000-0000-0000-000000000035', 'expiry.mark-unclaimed', 'Duyệt chuyển hàng không nhận sang quyên góp.', 'Duyệt hàng không nhận');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123123556_AddExpiryProcessingWorkflow') THEN
    INSERT INTO "RolePermissions" ("PermissionId", "RoleId")
    VALUES ('30000000-0000-0000-0000-000000000032', '20000000-0000-0000-0000-000000000001');
    INSERT INTO "RolePermissions" ("PermissionId", "RoleId")
    VALUES ('30000000-0000-0000-0000-000000000033', '20000000-0000-0000-0000-000000000001');
    INSERT INTO "RolePermissions" ("PermissionId", "RoleId")
    VALUES ('30000000-0000-0000-0000-000000000034', '20000000-0000-0000-0000-000000000001');
    INSERT INTO "RolePermissions" ("PermissionId", "RoleId")
    VALUES ('30000000-0000-0000-0000-000000000035', '20000000-0000-0000-0000-000000000001');
    INSERT INTO "RolePermissions" ("PermissionId", "RoleId")
    VALUES ('30000000-0000-0000-0000-000000000032', '20000000-0000-0000-0000-000000000002');
    INSERT INTO "RolePermissions" ("PermissionId", "RoleId")
    VALUES ('30000000-0000-0000-0000-000000000033', '20000000-0000-0000-0000-000000000002');
    INSERT INTO "RolePermissions" ("PermissionId", "RoleId")
    VALUES ('30000000-0000-0000-0000-000000000034', '20000000-0000-0000-0000-000000000002');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123123556_AddExpiryProcessingWorkflow') THEN
    CREATE INDEX "IX_ExpiryProcessings_UnclaimedApprovedByEmployeeId" ON "ExpiryProcessings" ("UnclaimedApprovedByEmployeeId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123123556_AddExpiryProcessingWorkflow') THEN
    ALTER TABLE "ExpiryProcessings" ADD CONSTRAINT "FK_ExpiryProcessings_EmployeeProfiles_UnclaimedApprovedByEmplo~" FOREIGN KEY ("UnclaimedApprovedByEmployeeId") REFERENCES "EmployeeProfiles" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123123556_AddExpiryProcessingWorkflow') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261123123556_AddExpiryProcessingWorkflow', '10.0.12');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123124015_AddExpiryProcessingItemEvidence') THEN
    ALTER TABLE "ExpiryProcessingItems" ADD "Evidence" character varying(1000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123124015_AddExpiryProcessingItemEvidence') THEN
    ALTER TABLE "ExpiryProcessingItems" ADD "HandledAt" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123124015_AddExpiryProcessingItemEvidence') THEN
    ALTER TABLE "ExpiryProcessingItems" ADD "HandledByEmployeeId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123124015_AddExpiryProcessingItemEvidence') THEN
    ALTER TABLE "ExpiryProcessingItems" ADD "Note" character varying(1000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123124015_AddExpiryProcessingItemEvidence') THEN
    ALTER TABLE "ExpiryProcessingItems" ADD "RecipientOrganization" character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123124015_AddExpiryProcessingItemEvidence') THEN
    CREATE INDEX "IX_ExpiryProcessingItems_HandledByEmployeeId" ON "ExpiryProcessingItems" ("HandledByEmployeeId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123124015_AddExpiryProcessingItemEvidence') THEN
    ALTER TABLE "ExpiryProcessingItems" ADD CONSTRAINT "FK_ExpiryProcessingItems_EmployeeProfiles_HandledByEmployeeId" FOREIGN KEY ("HandledByEmployeeId") REFERENCES "EmployeeProfiles" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123124015_AddExpiryProcessingItemEvidence') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261123124015_AddExpiryProcessingItemEvidence', '10.0.12');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123131001_AddDonationReceipts') THEN
    CREATE TABLE "DonationReceipts" (
        "Id" uuid NOT NULL,
        "ExpiryProcessingId" uuid NOT NULL,
        "ReceiptNumber" character varying(40) NOT NULL,
        "ContractNumber" character varying(100) NOT NULL,
        "CustomerName" character varying(200),
        "RecipientOrganization" character varying(200) NOT NULL,
        "Evidence" character varying(1000) NOT NULL,
        "Note" character varying(1000),
        "HandledByEmployeeId" uuid NOT NULL,
        "HandledAt" timestamp with time zone NOT NULL,
        "ProductsJson" jsonb NOT NULL,
        CONSTRAINT "PK_DonationReceipts" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_DonationReceipts_EmployeeProfiles_HandledByEmployeeId" FOREIGN KEY ("HandledByEmployeeId") REFERENCES "EmployeeProfiles" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_DonationReceipts_ExpiryProcessings_ExpiryProcessingId" FOREIGN KEY ("ExpiryProcessingId") REFERENCES "ExpiryProcessings" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123131001_AddDonationReceipts') THEN
    CREATE INDEX "IX_DonationReceipts_ExpiryProcessingId" ON "DonationReceipts" ("ExpiryProcessingId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123131001_AddDonationReceipts') THEN
    CREATE INDEX "IX_DonationReceipts_HandledByEmployeeId" ON "DonationReceipts" ("HandledByEmployeeId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123131001_AddDonationReceipts') THEN
    CREATE UNIQUE INDEX "IX_DonationReceipts_ReceiptNumber" ON "DonationReceipts" ("ReceiptNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123131001_AddDonationReceipts') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261123131001_AddDonationReceipts', '10.0.12');
    END IF;
END $EF$;
COMMIT;

