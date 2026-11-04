using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OCS.Api.Context.Migrations;

// The feature migration predates the English-name migration. Some databases have
// already applied the rename, so support both schemas without dropping data.
[DbContext(typeof(AppDbContext))]
[Migration("20261104100000_UpdateAppointmentStatusAndFields")]
public partial class UpdateAppointmentStatusAndFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE IF EXISTS "DATLICH" ADD COLUMN IF NOT EXISTS "CancellationReason" character varying(500);
            ALTER TABLE IF EXISTS "DATLICH" ADD COLUMN IF NOT EXISTS "Note" character varying(1000);
            ALTER TABLE IF EXISTS "DATLICH" ADD COLUMN IF NOT EXISTS "ReminderSentAt" timestamp with time zone;
            ALTER TABLE IF EXISTS "DATLICH" DROP CONSTRAINT IF EXISTS "CK_DATLICH_Enums";
            ALTER TABLE IF EXISTS "DATLICH" ADD CONSTRAINT "CK_DATLICH_Enums" CHECK ("Status" IN ('Scheduled', 'CheckedIn', 'Completed', 'Cancelled', 'NoShow') AND "SubmissionMethod" IN ('InPerson', 'Shipping'));

            ALTER TABLE IF EXISTS "Appointments" ADD COLUMN IF NOT EXISTS "CancellationReason" character varying(500);
            ALTER TABLE IF EXISTS "Appointments" ADD COLUMN IF NOT EXISTS "Note" character varying(1000);
            ALTER TABLE IF EXISTS "Appointments" ADD COLUMN IF NOT EXISTS "ReminderSentAt" timestamp with time zone;
            ALTER TABLE IF EXISTS "Appointments" DROP CONSTRAINT IF EXISTS "CK_Appointments_Enums";
            ALTER TABLE IF EXISTS "Appointments" ADD CONSTRAINT "CK_Appointments_Enums" CHECK ("Status" IN ('Scheduled', 'CheckedIn', 'Completed', 'Cancelled', 'NoShow') AND "SubmissionMethod" IN ('InPerson', 'Shipping'));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE IF EXISTS "DATLICH" DROP CONSTRAINT IF EXISTS "CK_DATLICH_Enums";
            ALTER TABLE IF EXISTS "DATLICH" DROP COLUMN IF EXISTS "CancellationReason";
            ALTER TABLE IF EXISTS "DATLICH" DROP COLUMN IF EXISTS "Note";
            ALTER TABLE IF EXISTS "DATLICH" DROP COLUMN IF EXISTS "ReminderSentAt";
            ALTER TABLE IF EXISTS "DATLICH" ADD CONSTRAINT "CK_DATLICH_Enums" CHECK ("Status" IN ('Scheduled', 'Cancelled', 'Completed') AND "SubmissionMethod" IN ('InPerson', 'Shipping'));

            ALTER TABLE IF EXISTS "Appointments" DROP CONSTRAINT IF EXISTS "CK_Appointments_Enums";
            ALTER TABLE IF EXISTS "Appointments" DROP COLUMN IF EXISTS "CancellationReason";
            ALTER TABLE IF EXISTS "Appointments" DROP COLUMN IF EXISTS "Note";
            ALTER TABLE IF EXISTS "Appointments" DROP COLUMN IF EXISTS "ReminderSentAt";
            ALTER TABLE IF EXISTS "Appointments" ADD CONSTRAINT "CK_Appointments_Enums" CHECK ("Status" IN ('Scheduled', 'Cancelled', 'Completed') AND "SubmissionMethod" IN ('InPerson', 'Shipping'));
            """);
    }
}
