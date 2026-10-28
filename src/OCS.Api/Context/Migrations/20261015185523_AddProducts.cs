using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OCS.Api.Context.Migrations
{
    /// <inheritdoc />
    public partial class AddProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DATLICH",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TimeSlotId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EstimatedItemCount = table.Column<int>(type: "integer", nullable: false),
                    SubmissionMethod = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TrackingNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DATLICH", x => x.Id);
                    table.CheckConstraint("CK_DATLICH_Enums", "\"Status\" IN ('Scheduled', 'Cancelled', 'Completed')\r\nAND \"SubmissionMethod\" IN ('InPerson', 'Shipping')");
                    table.CheckConstraint("CK_DATLICH_ItemCount", "\"EstimatedItemCount\" >= 0");
                    table.ForeignKey(
                        name: "FK_DATLICH_KHACHHANG_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "KHACHHANG",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DATLICH_KHUNGGIOHEN_TimeSlotId",
                        column: x => x.TimeSlotId,
                        principalTable: "KHUNGGIOHEN",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "THUMUA",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BuyoutNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    TotalPurchasePrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_THUMUA", x => x.Id);
                    table.CheckConstraint("CK_THUMUA_Approver", "\"Status\" <> 'Approved'\r\nOR \"ApprovedByEmployeeId\" IS NOT NULL");
                    table.CheckConstraint("CK_THUMUA_Status", "\"Status\" IN ('Pending', 'Approved', 'Rejected')");
                    table.CheckConstraint("CK_THUMUA_Total", "\"TotalPurchasePrice\" >= 0");
                    table.ForeignKey(
                        name: "FK_THUMUA_KHACHHANG_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "KHACHHANG",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_THUMUA_NHANVIEN_ApprovedByEmployeeId",
                        column: x => x.ApprovedByEmployeeId,
                        principalTable: "NHANVIEN",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_THUMUA_NHANVIEN_CreatedByEmployeeId",
                        column: x => x.CreatedByEmployeeId,
                        principalTable: "NHANVIEN",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HOPDONGKYGUI",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContractNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    SignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiryHandling = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HOPDONGKYGUI", x => x.Id);
                    table.CheckConstraint("CK_HOPDONGKYGUI_Dates", "\"EndsAt\" > \"SignedAt\"");
                    table.CheckConstraint("CK_HOPDONGKYGUI_Enums", "\"Status\" IN ('Active', 'Completed')\r\nAND \"ExpiryHandling\" IN ('Return', 'Donate')");
                    table.ForeignKey(
                        name: "FK_HOPDONGKYGUI_DATLICH_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "DATLICH",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HOPDONGKYGUI_KHACHHANG_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "KHACHHANG",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HOPDONGKYGUI_NHANVIEN_CreatedByEmployeeId",
                        column: x => x.CreatedByEmployeeId,
                        principalTable: "NHANVIEN",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SANPHAM",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Barcode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Size = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ConditionPercentage = table.Column<int>(type: "integer", nullable: false),
                    BrandId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ConsignmentContractId = table.Column<Guid>(type: "uuid", nullable: true),
                    BuyoutId = table.Column<Guid>(type: "uuid", nullable: true),
                    ListedPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PurchasePrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    AgreedCommissionRate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    AgreedBrandTier = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SANPHAM", x => x.Id);
                    table.CheckConstraint("CK_SANPHAM_Commission", "\"AgreedCommissionRate\" IS NULL\r\nOR \"AgreedCommissionRate\" BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_SANPHAM_Condition", "\"ConditionPercentage\" BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_SANPHAM_Enums", "\"Status\" IN (\r\n    'PendingProcessing', 'OnSale', 'Sold',\r\n    'Expired', 'Donated', 'Returned',\r\n    'EarlyWithdrawn', 'Compensated', 'Cancelled'\r\n)\r\nAND (\r\n    \"AgreedBrandTier\" IS NULL\r\n    OR \"AgreedBrandTier\" IN (\r\n        'NoBrand', 'LocalBrand',\r\n        'Commercial', 'Luxury'\r\n    )\r\n)");
                    table.CheckConstraint("CK_SANPHAM_Prices", "\"ListedPrice\" >= 0\r\nAND (\r\n    \"PurchasePrice\" IS NULL\r\n    OR \"PurchasePrice\" >= 0\r\n)");
                    table.CheckConstraint("CK_SANPHAM_Source", "(\r\n    \"SourceType\" = 'Consignment'\r\n    AND \"ConsignmentContractId\" IS NOT NULL\r\n    AND \"BuyoutId\" IS NULL\r\n    AND \"PurchasePrice\" IS NULL\r\n    AND \"AgreedCommissionRate\" IS NOT NULL\r\n    AND \"AgreedBrandTier\" IS NOT NULL\r\n)\r\nOR\r\n(\r\n    \"SourceType\" = 'Buyout'\r\n    AND \"ConsignmentContractId\" IS NULL\r\n    AND \"BuyoutId\" IS NOT NULL\r\n    AND \"PurchasePrice\" IS NOT NULL\r\n    AND \"AgreedCommissionRate\" IS NULL\r\n    AND \"AgreedBrandTier\" IS NULL\r\n)");
                    table.ForeignKey(
                        name: "FK_SANPHAM_DANHMUC_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "DANHMUC",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SANPHAM_HOPDONGKYGUI_ConsignmentContractId",
                        column: x => x.ConsignmentContractId,
                        principalTable: "HOPDONGKYGUI",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SANPHAM_KHOLUU_StorageLocationId",
                        column: x => x.StorageLocationId,
                        principalTable: "KHOLUU",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SANPHAM_THUMUA_BuyoutId",
                        column: x => x.BuyoutId,
                        principalTable: "THUMUA",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SANPHAM_THUONGHIEU_BrandId",
                        column: x => x.BrandId,
                        principalTable: "THUONGHIEU",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ANH_SANPHAM",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ImageType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ANH_SANPHAM", x => x.Id);
                    table.CheckConstraint("CK_ANH_SANPHAM_ImageType", "\"ImageType\" IN ('Cover', 'Detail', 'Incident')");
                    table.CheckConstraint("CK_ANH_SANPHAM_SortOrder", "\"SortOrder\" >= 0");
                    table.CheckConstraint("CK_ANH_SANPHAM_Url", "length(btrim(\"Url\")) > 0");
                    table.ForeignKey(
                        name: "FK_ANH_SANPHAM_SANPHAM_ProductId",
                        column: x => x.ProductId,
                        principalTable: "SANPHAM",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LICHSUTRANGTHAISP",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    NewStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ChangedByAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LICHSUTRANGTHAISP", x => x.Id);
                    table.CheckConstraint("CK_LICHSUTRANGTHAISP_DifferentStatus", "\"PreviousStatus\" IS NULL\nOR \"PreviousStatus\" <> \"NewStatus\"");
                    table.CheckConstraint("CK_LICHSUTRANGTHAISP_NewStatus", "\"NewStatus\" IN ('PendingProcessing', 'OnSale', 'Sold', 'Expired', 'Donated', 'Returned', 'EarlyWithdrawn', 'Compensated', 'Cancelled')");
                    table.CheckConstraint("CK_LICHSUTRANGTHAISP_PreviousStatus", "\"PreviousStatus\" IS NULL OR \"PreviousStatus\" IN ('PendingProcessing', 'OnSale', 'Sold', 'Expired', 'Donated', 'Returned', 'EarlyWithdrawn', 'Compensated', 'Cancelled')");
                    table.ForeignKey(
                        name: "FK_LICHSUTRANGTHAISP_SANPHAM_ProductId",
                        column: x => x.ProductId,
                        principalTable: "SANPHAM",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LICHSUTRANGTHAISP_TAIKHOAN_ChangedByAccountId",
                        column: x => x.ChangedByAccountId,
                        principalTable: "TAIKHOAN",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ANH_SANPHAM_ProductId",
                table: "ANH_SANPHAM",
                column: "ProductId",
                unique: true,
                filter: "\"ImageType\" = 'Cover'");

            migrationBuilder.CreateIndex(
                name: "IX_ANH_SANPHAM_ProductId_SortOrder",
                table: "ANH_SANPHAM",
                columns: new[] { "ProductId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_DATLICH_AppointmentDate_TimeSlotId_Status",
                table: "DATLICH",
                columns: new[] { "AppointmentDate", "TimeSlotId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DATLICH_CustomerId",
                table: "DATLICH",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_DATLICH_TimeSlotId",
                table: "DATLICH",
                column: "TimeSlotId");

            migrationBuilder.CreateIndex(
                name: "IX_HOPDONGKYGUI_AppointmentId",
                table: "HOPDONGKYGUI",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_HOPDONGKYGUI_ContractNumber",
                table: "HOPDONGKYGUI",
                column: "ContractNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HOPDONGKYGUI_CreatedByEmployeeId",
                table: "HOPDONGKYGUI",
                column: "CreatedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_HOPDONGKYGUI_CustomerId",
                table: "HOPDONGKYGUI",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_HOPDONGKYGUI_Status_EndsAt",
                table: "HOPDONGKYGUI",
                columns: new[] { "Status", "EndsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LICHSUTRANGTHAISP_ChangedByAccountId",
                table: "LICHSUTRANGTHAISP",
                column: "ChangedByAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_LICHSUTRANGTHAISP_ProductId_OccurredAt",
                table: "LICHSUTRANGTHAISP",
                columns: new[] { "ProductId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SANPHAM_Barcode",
                table: "SANPHAM",
                column: "Barcode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SANPHAM_BrandId",
                table: "SANPHAM",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_SANPHAM_BuyoutId",
                table: "SANPHAM",
                column: "BuyoutId");

            migrationBuilder.CreateIndex(
                name: "IX_SANPHAM_CategoryId",
                table: "SANPHAM",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_SANPHAM_ConsignmentContractId_Status",
                table: "SANPHAM",
                columns: new[] { "ConsignmentContractId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SANPHAM_Status_CategoryId",
                table: "SANPHAM",
                columns: new[] { "Status", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_SANPHAM_StorageLocationId",
                table: "SANPHAM",
                column: "StorageLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_THUMUA_ApprovedByEmployeeId",
                table: "THUMUA",
                column: "ApprovedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_THUMUA_BuyoutNumber",
                table: "THUMUA",
                column: "BuyoutNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_THUMUA_CreatedByEmployeeId",
                table: "THUMUA",
                column: "CreatedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_THUMUA_CustomerId",
                table: "THUMUA",
                column: "CustomerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ANH_SANPHAM");

            migrationBuilder.DropTable(
                name: "LICHSUTRANGTHAISP");

            migrationBuilder.DropTable(
                name: "SANPHAM");

            migrationBuilder.DropTable(
                name: "HOPDONGKYGUI");

            migrationBuilder.DropTable(
                name: "THUMUA");

            migrationBuilder.DropTable(
                name: "DATLICH");
        }
    }
}
