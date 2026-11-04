using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OCS.Api.Context.Migrations
{
    /// <inheritdoc />
    public partial class UpdateAppointmentStatusAndFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_DATLICH_Enums",
                table: "DATLICH");

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "DATLICH",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "DATLICH",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReminderSentAt",
                table: "DATLICH",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_DATLICH_Enums",
                table: "DATLICH",
                sql: "\"Status\" IN ('Scheduled', 'CheckedIn', 'Completed', 'Cancelled', 'NoShow')\r\nAND \"SubmissionMethod\" IN ('InPerson', 'Shipping')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_DATLICH_Enums",
                table: "DATLICH");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "DATLICH");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "DATLICH");

            migrationBuilder.DropColumn(
                name: "ReminderSentAt",
                table: "DATLICH");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DATLICH_Enums",
                table: "DATLICH",
                sql: "\"Status\" IN ('Scheduled', 'Cancelled', 'Completed')\r\nAND \"SubmissionMethod\" IN ('InPerson', 'Shipping')");
        }
    }
}

