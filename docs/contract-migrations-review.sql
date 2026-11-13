START TRANSACTION;
ALTER TABLE "ConsignmentContracts" ALTER COLUMN "CustomerId" DROP NOT NULL;

ALTER TABLE "ConsignmentContracts" ADD "BankAccountHolderSnapshot" character varying(200);

ALTER TABLE "ConsignmentContracts" ADD "BankAccountNumberSnapshot" character varying(50);

ALTER TABLE "ConsignmentContracts" ADD "BankCodeSnapshot" character varying(50);

ALTER TABLE "ConsignmentContracts" ADD "CustomerAddressSnapshot" character varying(500);

ALTER TABLE "ConsignmentContracts" ADD "CustomerEmailSnapshot" character varying(254);

ALTER TABLE "ConsignmentContracts" ADD "CustomerIdentitySnapshot" character varying(20);

ALTER TABLE "ConsignmentContracts" ADD "CustomerNameSnapshot" character varying(200);

ALTER TABLE "ConsignmentContracts" ADD "CustomerPhoneSnapshot" character varying(20);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261112173531_AddContractCustomerSnapshot', '10.0.12');

COMMIT;

START TRANSACTION;
ALTER TABLE "ProductImages" ADD "Content" bytea;

ALTER TABLE "ProductImages" ADD "ContentType" character varying(50);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261112175953_AddProductImageContentAndCustomerVersion', '10.0.12');

COMMIT;

START TRANSACTION;
INSERT INTO "Permissions" ("Id", "Code", "Description", "Name")
VALUES ('30000000-0000-0000-0000-000000000031', 'customers.update', 'Cập nhật thông tin liên hệ và nhận tiền của khách.', 'Cập nhật hồ sơ khách');

INSERT INTO "RolePermissions" ("PermissionId", "RoleId")
VALUES ('30000000-0000-0000-0000-000000000031', '20000000-0000-0000-0000-000000000001');
INSERT INTO "RolePermissions" ("PermissionId", "RoleId")
VALUES ('30000000-0000-0000-0000-000000000031', '20000000-0000-0000-0000-000000000002');

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261112180412_AddCustomerUpdatePermission', '10.0.12');

COMMIT;

START TRANSACTION;
CREATE TABLE "ContractDocuments" (
    "ContractId" uuid NOT NULL,
    "Content" bytea NOT NULL,
    "Terms" character varying(6000),
    "TemplateVersion" character varying(30) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ContractDocuments" PRIMARY KEY ("ContractId"),
    CONSTRAINT "FK_ContractDocuments_ConsignmentContracts_ContractId" FOREIGN KEY ("ContractId") REFERENCES "ConsignmentContracts" ("Id") ON DELETE RESTRICT
);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261112182242_AddContractPdfDocuments', '10.0.12');

COMMIT;

START TRANSACTION;
ALTER TABLE "ContractDocuments" ADD "SignatureContent" bytea;

ALTER TABLE "ContractDocuments" ADD "SignatureContentType" character varying(20);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261112182823_AddContractSignature', '10.0.12');

COMMIT;

START TRANSACTION;
ALTER TABLE "ContractDocuments" ALTER COLUMN "TemplateVersion" TYPE character varying(40);

CREATE TABLE "ContractTemplates" (
    "Id" integer NOT NULL,
    "Title" character varying(200) NOT NULL,
    "OrganizationName" character varying(200) NOT NULL,
    "HeaderText" character varying(1000) NOT NULL,
    "FooterText" character varying(1000) NOT NULL,
    "DefaultTerms" character varying(6000) NOT NULL,
    "Version" uuid NOT NULL,
    "UpdatedBy" uuid,
    "UpdatedAt" timestamp with time zone,
    CONSTRAINT "PK_ContractTemplates" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_ContractTemplates_Singleton" CHECK ("Id" = 1)
);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261112191559_AddContractTemplateManagement', '10.0.12');

COMMIT;
