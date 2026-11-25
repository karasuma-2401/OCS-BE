using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OCS.Api.Context;

#nullable disable

namespace OCS.Api.Context.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261124083000_BackfillApprovedIncidentPayouts")]
public partial class BackfillApprovedIncidentPayouts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            DECLARE
                rec RECORD;
                target_payout_id uuid;
                detail_id uuid;
                now_utc timestamptz := now();
            BEGIN
                FOR rec IN
                    SELECT
                        r."Id" AS incident_id,
                        r."CompensationAmount" AS compensation_amount,
                        r."ApprovedByEmployeeId" AS approved_by,
                        p."Name" AS product_name,
                        p."Barcode" AS product_barcode,
                        c."Id" AS contract_id,
                        COALESCE(NULLIF(BTRIM(c."BankCodeSnapshot"), ''), NULLIF(BTRIM(cp."BankCode"), '')) AS bank_code,
                        COALESCE(NULLIF(BTRIM(c."BankAccountNumberSnapshot"), ''), NULLIF(BTRIM(cp."BankAccountNumber"), '')) AS bank_account_number,
                        COALESCE(NULLIF(BTRIM(c."BankAccountHolderSnapshot"), ''), NULLIF(BTRIM(cp."BankAccountHolderName"), '')) AS bank_account_holder
                    FROM "IncidentReports" r
                    JOIN "Products" p ON p."Id" = r."ProductId"
                    JOIN "ConsignmentContracts" c ON c."Id" = p."ConsignmentContractId"
                    LEFT JOIN "CustomerProfiles" cp ON cp."Id" = c."CustomerId"
                    WHERE r."Status" = 'Approved'
                      AND c."Status" <> 'Cancelled'
                      AND r."CompensationAmount" > 0
                      AND NOT EXISTS (
                          SELECT 1 FROM "PayoutDetails" pd
                          WHERE pd."IncidentReportId" = r."Id" AND NOT pd."IsReleased"
                      )
                    ORDER BY r."ApprovedAt" NULLS LAST, r."CreatedAt"
                LOOP
                    SELECT py."Id"
                    INTO target_payout_id
                    FROM "Payouts" py
                    WHERE py."ConsignmentContractId" = rec.contract_id
                      AND py."Status" = 'Pending'
                    ORDER BY py."CreatedAt" DESC
                    LIMIT 1;

                    IF target_payout_id IS NULL THEN
                        target_payout_id := gen_random_uuid();
                        INSERT INTO "Payouts" (
                            "Id", "PayoutNumber", "ConsignmentContractId", "CreatedByEmployeeId",
                            "BankCode", "BankAccountNumber", "BankAccountHolder",
                            "TotalAmount", "Status", "CreatedAt"
                        )
                        VALUES (
                            target_payout_id,
                            'PO-' || to_char(now_utc AT TIME ZONE 'UTC', 'YYYYMMDDHH24MISS') || '-' || substr(replace(target_payout_id::text, '-', ''), 1, 9),
                            rec.contract_id,
                            rec.approved_by,
                            rec.bank_code,
                            rec.bank_account_number,
                            rec.bank_account_holder,
                            rec.compensation_amount,
                            'Pending',
                            now_utc
                        );
                    ELSE
                        UPDATE "Payouts"
                        SET "TotalAmount" = "TotalAmount" + rec.compensation_amount,
                            "BankCode" = COALESCE(NULLIF(BTRIM("BankCode"), ''), rec.bank_code),
                            "BankAccountNumber" = COALESCE(NULLIF(BTRIM("BankAccountNumber"), ''), rec.bank_account_number),
                            "BankAccountHolder" = COALESCE(NULLIF(BTRIM("BankAccountHolder"), ''), rec.bank_account_holder),
                            "UpdatedAt" = now_utc
                        WHERE "Id" = target_payout_id;
                    END IF;

                    detail_id := gen_random_uuid();
                    INSERT INTO "PayoutDetails" (
                        "Id", "PayoutId", "ProductId", "IncidentReportId",
                        "Type", "Description", "Amount", "IsReleased", "CreatedAt"
                    )
                    VALUES (
                        detail_id,
                        target_payout_id,
                        NULL,
                        rec.incident_id,
                        'Compensation',
                        'Bồi thường sự cố: ' || rec.product_name || ' (' || rec.product_barcode || ')',
                        rec.compensation_amount,
                        false,
                        now_utc
                    );
                END LOOP;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Preserve generated payout statements on rollback.
    }
}
