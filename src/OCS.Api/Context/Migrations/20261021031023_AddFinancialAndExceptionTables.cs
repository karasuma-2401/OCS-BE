using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OCS.Api.Context.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialAndExceptionTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_HOPDONGKYGUI_Enums",
                table: "HOPDONGKYGUI");

            migrationBuilder.AddColumn<DateOnly>(
                name: "LeftOn",
                table: "NHANVIEN",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BIENBANSUCO",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CompensationAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BIENBANSUCO", x => x.Id);
                    table.CheckConstraint("CK_BIENBANSUCO_Compensation", "\"CompensationAmount\" >= 0");
                    table.CheckConstraint("CK_BIENBANSUCO_Enums", "\"Type\" IN ('Lost', 'Damaged', 'TagSwapped') AND \"Status\" IN ('Pending', 'Approved', 'Compensated')");
                    table.ForeignKey(
                        name: "FK_BIENBANSUCO_NHANVIEN_ApprovedByEmployeeId",
                        column: x => x.ApprovedByEmployeeId,
                        principalTable: "NHANVIEN",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BIENBANSUCO_NHANVIEN_CreatedByEmployeeId",
                        column: x => x.CreatedByEmployeeId,
                        principalTable: "NHANVIEN",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BIENBANSUCO_SANPHAM_ProductId",
                        column: x => x.ProductId,
                        principalTable: "SANPHAM",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HOADONBAN",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SoldByEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaymentStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HOADONBAN", x => x.Id);
                    table.CheckConstraint("CK_HOADONBAN_PaymentStatus", "\"PaymentStatus\" IN ('Unpaid', 'Partial', 'Paid')");
                    table.CheckConstraint("CK_HOADONBAN_Total", "\"TotalAmount\" >= 0");
                    table.ForeignKey(
                        name: "FK_HOADONBAN_NHANVIEN_SoldByEmployeeId",
                        column: x => x.SoldByEmployeeId,
                        principalTable: "NHANVIEN",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PHIEUGIAINGAN",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PayoutNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ConsignmentContractId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ProofUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    BankTransactionReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PHIEUGIAINGAN", x => x.Id);
                    table.CheckConstraint("CK_PHIEUGIAINGAN_Status", "\"Status\" IN ('Pending', 'Transferred', 'Completed')");
                    table.CheckConstraint("CK_PHIEUGIAINGAN_Total", "\"TotalAmount\" >= 0");
                    table.ForeignKey(
                        name: "FK_PHIEUGIAINGAN_HOPDONGKYGUI_ConsignmentContractId",
                        column: x => x.ConsignmentContractId,
                        principalTable: "HOPDONGKYGUI",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PHIEUGIAINGAN_NHANVIEN_ApprovedByEmployeeId",
                        column: x => x.ApprovedByEmployeeId,
                        principalTable: "NHANVIEN",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PHIEURUTHANGSOM",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    TotalPenaltyAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaymentStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PHIEURUTHANGSOM", x => x.Id);
                    table.CheckConstraint("CK_PHIEURUTHANGSOM_PaymentStatus", "\"PaymentStatus\" IN ('Unpaid', 'Partial', 'Paid')");
                    table.CheckConstraint("CK_PHIEURUTHANGSOM_TotalPenalty", "\"TotalPenaltyAmount\" >= 0");
                    table.ForeignKey(
                        name: "FK_PHIEURUTHANGSOM_KHACHHANG_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "KHACHHANG",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PHIEURUTHANGSOM_NHANVIEN_ProcessedByEmployeeId",
                        column: x => x.ProcessedByEmployeeId,
                        principalTable: "NHANVIEN",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PHIEUXULYHETHAN",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectionDeadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PHIEUXULYHETHAN", x => x.Id);
                    table.CheckConstraint("CK_PHIEUXULYHETHAN_Enums", "\"Type\" IN ('Donate', 'Return') AND \"Status\" IN ('Pending', 'Completed', 'OverdueDonated')");
                    table.ForeignKey(
                        name: "FK_PHIEUXULYHETHAN_NHANVIEN_ProcessedByEmployeeId",
                        column: x => x.ProcessedByEmployeeId,
                        principalTable: "NHANVIEN",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "THAMSO",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_THAMSO", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "THONGBAO",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Content = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_THONGBAO", x => x.Id);
                    table.CheckConstraint("CK_THONGBAO_Type", "\"Type\" IN ('System', 'Manual', 'Promotion')");
                    table.ForeignKey(
                        name: "FK_THONGBAO_TAIKHOAN_RecipientAccountId",
                        column: x => x.RecipientAccountId,
                        principalTable: "TAIKHOAN",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_THONGBAO_TAIKHOAN_SenderAccountId",
                        column: x => x.SenderAccountId,
                        principalTable: "TAIKHOAN",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CT_HOADON",
                columns: table => new
                {
                    SaleInvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalePrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CT_HOADON", x => new { x.SaleInvoiceId, x.ProductId });
                    table.CheckConstraint("CK_CT_HOADON_SalePrice", "\"SalePrice\" >= 0");
                    table.ForeignKey(
                        name: "FK_CT_HOADON_HOADONBAN_SaleInvoiceId",
                        column: x => x.SaleInvoiceId,
                        principalTable: "HOADONBAN",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CT_HOADON_SANPHAM_ProductId",
                        column: x => x.ProductId,
                        principalTable: "SANPHAM",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CT_GIAINGAN",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PayoutId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    IncidentReportId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CT_GIAINGAN", x => x.Id);
                    table.CheckConstraint("CK_CT_GIAINGAN_Source", "NOT (\"ProductId\" IS NOT NULL AND \"IncidentReportId\" IS NOT NULL)");
                    table.CheckConstraint("CK_CT_GIAINGAN_Type", "\"Type\" IN ('Commission', 'Compensation', 'ServiceFee')");
                    table.ForeignKey(
                        name: "FK_CT_GIAINGAN_BIENBANSUCO_IncidentReportId",
                        column: x => x.IncidentReportId,
                        principalTable: "BIENBANSUCO",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CT_GIAINGAN_PHIEUGIAINGAN_PayoutId",
                        column: x => x.PayoutId,
                        principalTable: "PHIEUGIAINGAN",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CT_GIAINGAN_SANPHAM_ProductId",
                        column: x => x.ProductId,
                        principalTable: "SANPHAM",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CT_RUTSOM",
                columns: table => new
                {
                    EarlyWithdrawalId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ListedPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PenaltyAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CT_RUTSOM", x => new { x.EarlyWithdrawalId, x.ProductId });
                    table.CheckConstraint("CK_CT_RUTSOM_ListedPrice", "\"ListedPrice\" >= 0");
                    table.CheckConstraint("CK_CT_RUTSOM_Penalty", "\"PenaltyAmount\" >= 0");
                    table.ForeignKey(
                        name: "FK_CT_RUTSOM_PHIEURUTHANGSOM_EarlyWithdrawalId",
                        column: x => x.EarlyWithdrawalId,
                        principalTable: "PHIEURUTHANGSOM",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CT_RUTSOM_SANPHAM_ProductId",
                        column: x => x.ProductId,
                        principalTable: "SANPHAM",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GIAODICHTAICHINH",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SaleInvoiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    PayoutId = table.Column<Guid>(type: "uuid", nullable: true),
                    BuyoutId = table.Column<Guid>(type: "uuid", nullable: true),
                    EarlyWithdrawalId = table.Column<Guid>(type: "uuid", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaymentMethod = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BankTransactionReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProofUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PerformedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GIAODICHTAICHINH", x => x.Id);
                    table.CheckConstraint("CK_GIAODICHTAICHINH_Amount", "\"Amount\" >= 0");
                    table.CheckConstraint("CK_GIAODICHTAICHINH_Enums", "\"Type\" IN ('Income', 'Expense', 'CashFloat', 'BankDeposit') AND \"PaymentMethod\" IN ('Cash', 'VietQr', 'BankTransfer') AND \"Status\" IN ('Pending', 'Success', 'Failed', 'Refunded')");
                    table.ForeignKey(
                        name: "FK_GIAODICHTAICHINH_HOADONBAN_SaleInvoiceId",
                        column: x => x.SaleInvoiceId,
                        principalTable: "HOADONBAN",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GIAODICHTAICHINH_KHACHHANG_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "KHACHHANG",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GIAODICHTAICHINH_NHANVIEN_PerformedByEmployeeId",
                        column: x => x.PerformedByEmployeeId,
                        principalTable: "NHANVIEN",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GIAODICHTAICHINH_PHIEUGIAINGAN_PayoutId",
                        column: x => x.PayoutId,
                        principalTable: "PHIEUGIAINGAN",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GIAODICHTAICHINH_PHIEURUTHANGSOM_EarlyWithdrawalId",
                        column: x => x.EarlyWithdrawalId,
                        principalTable: "PHIEURUTHANGSOM",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GIAODICHTAICHINH_THUMUA_BuyoutId",
                        column: x => x.BuyoutId,
                        principalTable: "THUMUA",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CT_XULY",
                columns: table => new
                {
                    ExpiryProcessingId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CT_XULY", x => new { x.ExpiryProcessingId, x.ProductId });
                    table.ForeignKey(
                        name: "FK_CT_XULY_PHIEUXULYHETHAN_ExpiryProcessingId",
                        column: x => x.ExpiryProcessingId,
                        principalTable: "PHIEUXULYHETHAN",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CT_XULY_SANPHAM_ProductId",
                        column: x => x.ProductId,
                        principalTable: "SANPHAM",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "NHANVIEN",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000001"),
                column: "LeftOn",
                value: null);

            migrationBuilder.UpdateData(
                table: "NHANVIEN",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000002"),
                column: "LeftOn",
                value: null);

            migrationBuilder.AddCheckConstraint(
                name: "CK_HOPDONGKYGUI_Enums",
                table: "HOPDONGKYGUI",
                sql: "\"Status\" IN ('Active', 'Completed', 'Cancelled')\r\nAND \"ExpiryHandling\" IN ('Return', 'Donate')");

            migrationBuilder.CreateIndex(
                name: "IX_BIENBANSUCO_ApprovedByEmployeeId",
                table: "BIENBANSUCO",
                column: "ApprovedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_BIENBANSUCO_CreatedByEmployeeId",
                table: "BIENBANSUCO",
                column: "CreatedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_BIENBANSUCO_ProductId",
                table: "BIENBANSUCO",
                column: "ProductId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CT_GIAINGAN_IncidentReportId",
                table: "CT_GIAINGAN",
                column: "IncidentReportId");

            migrationBuilder.CreateIndex(
                name: "IX_CT_GIAINGAN_PayoutId",
                table: "CT_GIAINGAN",
                column: "PayoutId");

            migrationBuilder.CreateIndex(
                name: "IX_CT_GIAINGAN_ProductId",
                table: "CT_GIAINGAN",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_CT_HOADON_ProductId",
                table: "CT_HOADON",
                column: "ProductId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CT_RUTSOM_ProductId",
                table: "CT_RUTSOM",
                column: "ProductId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CT_XULY_ProductId",
                table: "CT_XULY",
                column: "ProductId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GIAODICHTAICHINH_BuyoutId",
                table: "GIAODICHTAICHINH",
                column: "BuyoutId");

            migrationBuilder.CreateIndex(
                name: "IX_GIAODICHTAICHINH_CustomerId",
                table: "GIAODICHTAICHINH",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_GIAODICHTAICHINH_EarlyWithdrawalId",
                table: "GIAODICHTAICHINH",
                column: "EarlyWithdrawalId");

            migrationBuilder.CreateIndex(
                name: "IX_GIAODICHTAICHINH_PayoutId",
                table: "GIAODICHTAICHINH",
                column: "PayoutId");

            migrationBuilder.CreateIndex(
                name: "IX_GIAODICHTAICHINH_PerformedByEmployeeId",
                table: "GIAODICHTAICHINH",
                column: "PerformedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_GIAODICHTAICHINH_SaleInvoiceId",
                table: "GIAODICHTAICHINH",
                column: "SaleInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_GIAODICHTAICHINH_Status_OccurredAt",
                table: "GIAODICHTAICHINH",
                columns: new[] { "Status", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HOADONBAN_InvoiceNumber",
                table: "HOADONBAN",
                column: "InvoiceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HOADONBAN_SoldByEmployeeId",
                table: "HOADONBAN",
                column: "SoldByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PHIEUGIAINGAN_ApprovedByEmployeeId",
                table: "PHIEUGIAINGAN",
                column: "ApprovedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PHIEUGIAINGAN_ConsignmentContractId",
                table: "PHIEUGIAINGAN",
                column: "ConsignmentContractId");

            migrationBuilder.CreateIndex(
                name: "IX_PHIEUGIAINGAN_PayoutNumber",
                table: "PHIEUGIAINGAN",
                column: "PayoutNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PHIEURUTHANGSOM_CustomerId",
                table: "PHIEURUTHANGSOM",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_PHIEURUTHANGSOM_ProcessedByEmployeeId",
                table: "PHIEURUTHANGSOM",
                column: "ProcessedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PHIEUXULYHETHAN_ProcessedByEmployeeId",
                table: "PHIEUXULYHETHAN",
                column: "ProcessedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_THONGBAO_RecipientAccountId_IsRead_SentAt",
                table: "THONGBAO",
                columns: new[] { "RecipientAccountId", "IsRead", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_THONGBAO_SenderAccountId",
                table: "THONGBAO",
                column: "SenderAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CT_GIAINGAN");

            migrationBuilder.DropTable(
                name: "CT_HOADON");

            migrationBuilder.DropTable(
                name: "CT_RUTSOM");

            migrationBuilder.DropTable(
                name: "CT_XULY");

            migrationBuilder.DropTable(
                name: "GIAODICHTAICHINH");

            migrationBuilder.DropTable(
                name: "THAMSO");

            migrationBuilder.DropTable(
                name: "THONGBAO");

            migrationBuilder.DropTable(
                name: "BIENBANSUCO");

            migrationBuilder.DropTable(
                name: "PHIEUXULYHETHAN");

            migrationBuilder.DropTable(
                name: "HOADONBAN");

            migrationBuilder.DropTable(
                name: "PHIEUGIAINGAN");

            migrationBuilder.DropTable(
                name: "PHIEURUTHANGSOM");

            migrationBuilder.DropCheckConstraint(
                name: "CK_HOPDONGKYGUI_Enums",
                table: "HOPDONGKYGUI");

            migrationBuilder.DropColumn(
                name: "LeftOn",
                table: "NHANVIEN");

            migrationBuilder.AddCheckConstraint(
                name: "CK_HOPDONGKYGUI_Enums",
                table: "HOPDONGKYGUI",
                sql: "\"Status\" IN ('Active', 'Completed')\r\nAND \"ExpiryHandling\" IN ('Return', 'Donate')");
        }
    }
}
