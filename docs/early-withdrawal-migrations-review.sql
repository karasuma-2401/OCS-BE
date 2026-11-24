START TRANSACTION;
DROP INDEX "IX_EarlyWithdrawals_CustomerId";

ALTER TABLE "Products" ADD "IsReservedForWithdrawal" boolean NOT NULL DEFAULT FALSE;

ALTER TABLE "EarlyWithdrawals" ALTER COLUMN "ProcessedByEmployeeId" DROP NOT NULL;

ALTER TABLE "EarlyWithdrawals" ADD "ApprovedAt" timestamp with time zone;

ALTER TABLE "EarlyWithdrawals" ADD "DecisionNote" character varying(1000);

ALTER TABLE "EarlyWithdrawals" ADD "HandedOverAt" timestamp with time zone;

ALTER TABLE "EarlyWithdrawals" ADD "HandedOverByEmployeeId" uuid;

ALTER TABLE "EarlyWithdrawals" ADD "HandoverEvidence" character varying(1000);

ALTER TABLE "EarlyWithdrawals" ADD "PaidAt" timestamp with time zone;

ALTER TABLE "EarlyWithdrawals" ADD "PaidByEmployeeId" uuid;

ALTER TABLE "EarlyWithdrawals" ADD "Reason" character varying(1000) NOT NULL DEFAULT '';

ALTER TABLE "EarlyWithdrawals" ADD "RequestedAt" timestamp with time zone NOT NULL DEFAULT TIMESTAMPTZ '-infinity';

ALTER TABLE "EarlyWithdrawals" ADD "Status" character varying(20) NOT NULL DEFAULT 'Legacy';

ALTER TABLE "EarlyWithdrawals" ADD "Version" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';

ALTER TABLE "EarlyWithdrawalItems" ADD "PenaltyRate" numeric(5,2) NOT NULL DEFAULT 0.0;

ALTER TABLE "ConsignmentContracts" ADD "EarlyWithdrawalFeeRate" numeric(5,2);

UPDATE "EarlyWithdrawals" SET "RequestedAt" = "CreatedAt" WHERE "Status" = 'Legacy';

INSERT INTO "Permissions" ("Id", "Code", "Description", "Name")
VALUES ('30000000-0000-0000-0000-000000001001', 'withdrawals.read', 'Xem yêu cầu rút hàng sớm.', 'Xem yêu cầu rút sớm');
INSERT INTO "Permissions" ("Id", "Code", "Description", "Name")
VALUES ('30000000-0000-0000-0000-000000001002', 'withdrawals.approve', 'Duyệt và từ chối yêu cầu rút sớm.', 'Duyệt rút sớm');
INSERT INTO "Permissions" ("Id", "Code", "Description", "Name")
VALUES ('30000000-0000-0000-0000-000000001003', 'withdrawals.payment', 'Ghi nhận thu đủ phí rút sớm.', 'Thu phí rút sớm');
INSERT INTO "Permissions" ("Id", "Code", "Description", "Name")
VALUES ('30000000-0000-0000-0000-000000001004', 'withdrawals.handover', 'Xác nhận giao hàng rút sớm.', 'Giao hàng rút sớm');

INSERT INTO "RolePermissions" ("PermissionId", "RoleId")
VALUES ('30000000-0000-0000-0000-000000001001', '20000000-0000-0000-0000-000000000001');
INSERT INTO "RolePermissions" ("PermissionId", "RoleId")
VALUES ('30000000-0000-0000-0000-000000001002', '20000000-0000-0000-0000-000000000001');
INSERT INTO "RolePermissions" ("PermissionId", "RoleId")
VALUES ('30000000-0000-0000-0000-000000001003', '20000000-0000-0000-0000-000000000001');
INSERT INTO "RolePermissions" ("PermissionId", "RoleId")
VALUES ('30000000-0000-0000-0000-000000001004', '20000000-0000-0000-0000-000000000001');
INSERT INTO "RolePermissions" ("PermissionId", "RoleId")
VALUES ('30000000-0000-0000-0000-000000001001', '20000000-0000-0000-0000-000000000002');
INSERT INTO "RolePermissions" ("PermissionId", "RoleId")
VALUES ('30000000-0000-0000-0000-000000001002', '20000000-0000-0000-0000-000000000002');
INSERT INTO "RolePermissions" ("PermissionId", "RoleId")
VALUES ('30000000-0000-0000-0000-000000001003', '20000000-0000-0000-0000-000000000002');
INSERT INTO "RolePermissions" ("PermissionId", "RoleId")
VALUES ('30000000-0000-0000-0000-000000001004', '20000000-0000-0000-0000-000000000002');

CREATE INDEX "IX_EarlyWithdrawals_CustomerId_RequestedAt" ON "EarlyWithdrawals" ("CustomerId", "RequestedAt");

CREATE INDEX "IX_EarlyWithdrawals_HandedOverByEmployeeId" ON "EarlyWithdrawals" ("HandedOverByEmployeeId");

CREATE INDEX "IX_EarlyWithdrawals_PaidByEmployeeId" ON "EarlyWithdrawals" ("PaidByEmployeeId");

ALTER TABLE "EarlyWithdrawals" ADD CONSTRAINT "CK_EarlyWithdrawals_Status" CHECK ("Status" IN ('Pending', 'Approved', 'Rejected', 'Cancelled', 'Completed', 'Legacy'));

ALTER TABLE "EarlyWithdrawalItems" ADD CONSTRAINT "CK_EarlyWithdrawalItems_Rate" CHECK ("PenaltyRate" BETWEEN 0 AND 100);

ALTER TABLE "ConsignmentContracts" ADD CONSTRAINT "CK_ConsignmentContracts_WithdrawalFeeRate" CHECK ("EarlyWithdrawalFeeRate" IS NULL OR "EarlyWithdrawalFeeRate" BETWEEN 0 AND 100);

ALTER TABLE "EarlyWithdrawals" ADD CONSTRAINT "FK_EarlyWithdrawals_EmployeeProfiles_HandedOverByEmployeeId" FOREIGN KEY ("HandedOverByEmployeeId") REFERENCES "EmployeeProfiles" ("Id") ON DELETE RESTRICT;

ALTER TABLE "EarlyWithdrawals" ADD CONSTRAINT "FK_EarlyWithdrawals_EmployeeProfiles_PaidByEmployeeId" FOREIGN KEY ("PaidByEmployeeId") REFERENCES "EmployeeProfiles" ("Id") ON DELETE RESTRICT;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261124125038_AddEarlyWithdrawalWorkflow', '10.0.12');

COMMIT;

