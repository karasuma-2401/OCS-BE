START TRANSACTION;
ALTER TABLE "ContractTemplates" ADD "FieldMappingsJson" text NOT NULL DEFAULT '[]';

ALTER TABLE "ContractTemplates" ADD "PdfBackground" bytea;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261113043305_AddContractPdfBackground', '10.0.12');

COMMIT;
