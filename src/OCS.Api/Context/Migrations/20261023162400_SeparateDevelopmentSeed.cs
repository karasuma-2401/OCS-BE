using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OCS.Api.Context.Migrations;

public partial class SeparateDevelopmentSeed : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Preserve applied migrations and remove only the complete, untouched legacy dataset.
        migrationBuilder.Sql("""
            DO $cleanup$
            DECLARE
                demo_ids uuid[] := ARRAY[
                    '10000000-0000-0000-0000-000000000001'::uuid,
                    '10000000-0000-0000-0000-000000000002'::uuid,
                    '10000000-0000-0000-0000-000000000003'::uuid
                ];
                pristine boolean;
                referenced boolean;
                fk record;
            BEGIN
                LOCK TABLE "TAIKHOAN", "KHACHHANG", "NHANVIEN", "TAIKHOAN_VAITRO"
                    IN SHARE ROW EXCLUSIVE MODE;
                SELECT count(*) = 3 INTO pristine
                FROM "TAIKHOAN" a
                JOIN (VALUES
                    ('10000000-0000-0000-0000-000000000001'::uuid, 'Internal', 'Quản lý demo', '0900000001', 'manager.demo', 'manager.demo@example.com', NULL::uuid),
                    ('10000000-0000-0000-0000-000000000002'::uuid, 'Internal', 'Nhân viên demo', '0900000002', 'staff.demo', 'staff.demo@example.com', '10000000-0000-0000-0000-000000000001'::uuid),
                    ('10000000-0000-0000-0000-000000000003'::uuid, 'Customer', 'Khách ký gửi demo', '0900000003', NULL, 'customer.demo@example.com', NULL::uuid)
                ) AS expected(id, kind, full_name, phone, username, email, creator) ON a."Id" = expected.id
                WHERE a."AccountType" = expected.kind AND a."FullName" = expected.full_name
                  AND a."PhoneNumber" = expected.phone AND a."Email" = expected.email
                  AND a."Username" IS NOT DISTINCT FROM expected.username
                  AND a."CreatedByAccountId" IS NOT DISTINCT FROM expected.creator
                  AND a."Status" = 'PendingActivation' AND a."PasswordHash" IS NULL
                  AND a."IdentityNumber" IS NULL AND a."PhoneVerifiedAt" IS NULL
                  AND a."ActivatedAt" IS NULL AND a."UpdatedAt" IS NULL
                  AND a."CreatedAt" = TIMESTAMPTZ '2026-10-16 00:00:00+00';

                pristine := pristine
                    AND (SELECT count(*) = 2 FROM "NHANVIEN"
                         WHERE ("Id", "JobTitle", "HiredOn") IN (
                             (demo_ids[1], 'Quản lý cửa hàng', DATE '2026-10-01'),
                             (demo_ids[2], 'Nhân viên POS', DATE '2026-10-10'))
                         AND "LeftOn" IS NULL AND "Status" = 'Working')
                    AND EXISTS (SELECT 1 FROM "KHACHHANG" WHERE "Id" = demo_ids[3]
                         AND "Address" = 'Địa chỉ mẫu tại TP. Hồ Chí Minh'
                         AND "BankCode" = 'VCB' AND "BankAccountNumber" = '000000000001'
                         AND "BankAccountHolderName" = 'KHACH KY GUI DEMO' AND "Status" = 'Active')
                    AND NOT EXISTS (SELECT 1 FROM "KHACHHANG" WHERE "Id" = ANY(demo_ids[1:2]))
                    AND NOT EXISTS (SELECT 1 FROM "NHANVIEN" WHERE "Id" = demo_ids[3])
                    AND (SELECT count(*) = 3 FROM "TAIKHOAN_VAITRO" WHERE "AccountId" = ANY(demo_ids))
                    AND (SELECT count(*) = 3 FROM "TAIKHOAN_VAITRO"
                         WHERE ("AccountId", "RoleId") IN (
                             (demo_ids[1], '20000000-0000-0000-0000-000000000001'::uuid),
                             (demo_ids[2], '20000000-0000-0000-0000-000000000002'::uuid),
                             (demo_ids[3], '20000000-0000-0000-0000-000000000004'::uuid)))
                    AND NOT EXISTS (SELECT 1 FROM "TAIKHOAN"
                         WHERE "CreatedByAccountId" = ANY(demo_ids) AND NOT ("Id" = ANY(demo_ids)));

                IF pristine THEN
                    -- Check all actual foreign keys, including audit and future business tables.
                    FOR fk IN
                        SELECT c.conrelid::regclass AS relation_name, a.attname AS column_name
                        FROM pg_constraint c
                        JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = ANY(c.conkey)
                        WHERE c.contype = 'f'
                          AND c.confrelid IN ('"TAIKHOAN"'::regclass, '"KHACHHANG"'::regclass, '"NHANVIEN"'::regclass)
                          AND c.conrelid NOT IN ('"TAIKHOAN"'::regclass, '"KHACHHANG"'::regclass,
                                                '"NHANVIEN"'::regclass, '"TAIKHOAN_VAITRO"'::regclass)
                    LOOP
                        EXECUTE format('SELECT EXISTS (SELECT 1 FROM %s WHERE %I = ANY($1))',
                            fk.relation_name, fk.column_name) INTO referenced USING demo_ids;
                        IF referenced THEN
                            pristine := false;
                            EXIT;
                        END IF;
                    END LOOP;
                END IF;

                IF pristine THEN
                    DELETE FROM "TAIKHOAN_VAITRO" WHERE "AccountId" = ANY(demo_ids);
                    DELETE FROM "KHACHHANG" WHERE "Id" = demo_ids[3];
                    DELETE FROM "NHANVIEN" WHERE "Id" = ANY(demo_ids[1:2]);
                    DELETE FROM "TAIKHOAN" WHERE "Id" = ANY(demo_ids[2:3]);
                    DELETE FROM "TAIKHOAN" WHERE "Id" = demo_ids[1];
                END IF;

                -- Remove only the known synthetic event, not real user history.
                DELETE FROM "NHATKYHETHONG"
                WHERE "Id" = '40000000-0000-0000-0000-000000000001'::uuid
                  AND "Action" = 'DemoDataSeeded' AND "Source" = 'DemoSeed'
                  AND "ActorAccountId" IS NULL
                  AND "ActorType" = 'System' AND "EntityType" = 'Account'
                  AND "EntityId" = demo_ids[1]::text
                  AND "IpAddress" IS NULL AND "CorrelationId" IS NULL
                  AND "OccurredAt" = TIMESTAMPTZ '2026-10-16 00:00:00+00'
                  AND "RecordedAt" = TIMESTAMPTZ '2026-10-16 00:00:00+00'
                  AND "Reason" = 'Bản ghi minh họa thuộc bộ dữ liệu demo; không phải sự kiện người dùng thực tế.'
                  AND "Changes"::jsonb = '{"Dataset":{"old":null,"new":"AccountsDemoV1"}}'::jsonb;
            END;
            $cleanup$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Rollback does not recreate demo identities. Development/Test can run --seed-demo explicitly.
    }
}
