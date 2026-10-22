using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace OrderManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mProduct",
                columns: table => new
                {
                    uidProductUUID = table.Column<Guid>(type: "uuid", nullable: false),
                    txtName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    intStockQuantity = table.Column<int>(type: "integer", nullable: false),
                    decPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    bolActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    bolDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    dtInserted = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    txtInsertedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: ""),
                    dtUpdated = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    txtUpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mProduct", x => x.uidProductUUID);
                    table.CheckConstraint("CK_mProduct_decPrice", "\"decPrice\" >= 0");
                    table.CheckConstraint("CK_mProduct_intStockQuantity", "\"intStockQuantity\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "mUser",
                columns: table => new
                {
                    uidUserUUID = table.Column<Guid>(type: "uuid", nullable: false),
                    txtUsername = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    txtDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    txtPasswordHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    txtRole = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    bolActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    bolDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    dtInserted = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    txtInsertedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: ""),
                    dtUpdated = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    txtUpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mUser", x => x.uidUserUUID);
                    table.CheckConstraint("CK_mUser_txtRole", "\"txtRole\" IN ('Admin','Sales')");
                });

            migrationBuilder.CreateTable(
                name: "trIdempotencyKey",
                columns: table => new
                {
                    uidIdempotencyKeyUUID = table.Column<Guid>(type: "uuid", nullable: false),
                    txtCustomerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    txtIdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    txtRequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    uidOrderUUID = table.Column<Guid>(type: "uuid", nullable: false),
                    bolActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    bolDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    dtInserted = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    txtInsertedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: ""),
                    dtUpdated = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    txtUpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trIdempotencyKey", x => x.uidIdempotencyKeyUUID);
                });

            migrationBuilder.CreateTable(
                name: "trOrder",
                columns: table => new
                {
                    uidOrderUUID = table.Column<Guid>(type: "uuid", nullable: false),
                    txtCustomerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    txtShippingAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    txtStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    decTotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    intVersion = table.Column<int>(type: "integer", nullable: false),
                    bolActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    bolDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    dtInserted = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    txtInsertedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: ""),
                    dtUpdated = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    txtUpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trOrder", x => x.uidOrderUUID);
                    table.CheckConstraint("CK_trOrder_decTotalAmount", "\"decTotalAmount\" >= 0");
                    table.CheckConstraint("CK_trOrder_txtStatus", "\"txtStatus\" IN ('Pending','Confirmed','Shipped','Delivered','Cancelled')");
                });

            migrationBuilder.CreateTable(
                name: "trOrderItem",
                columns: table => new
                {
                    uidOrderItemUUID = table.Column<Guid>(type: "uuid", nullable: false),
                    uidOrderUUID = table.Column<Guid>(type: "uuid", nullable: false),
                    uidProductUUID = table.Column<Guid>(type: "uuid", nullable: false),
                    txtProductName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    decUnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    intQuantity = table.Column<int>(type: "integer", nullable: false),
                    decLineTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    bolActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    bolDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    dtInserted = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    txtInsertedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: ""),
                    dtUpdated = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    txtUpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trOrderItem", x => x.uidOrderItemUUID);
                    table.CheckConstraint("CK_trOrderItem_intQuantity", "\"intQuantity\" > 0");
                    table.ForeignKey(
                        name: "FK_trOrderItem_mProduct",
                        column: x => x.uidProductUUID,
                        principalTable: "mProduct",
                        principalColumn: "uidProductUUID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trOrderItem_trOrder",
                        column: x => x.uidOrderUUID,
                        principalTable: "trOrder",
                        principalColumn: "uidOrderUUID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "mProduct",
                columns: new[] { "uidProductUUID", "dtInserted", "txtInsertedBy", "bolActive", "bolDeleted", "txtName", "decPrice", "intStockQuantity", "dtUpdated", "txtUpdatedBy" },
                values: new object[,]
                {
                    { new Guid("a1000000-0000-4000-8000-000000000001"), new DateTimeOffset(new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", true, false, "Product X", 100000m, 15, null, null },
                    { new Guid("a1000000-0000-4000-8000-000000000002"), new DateTimeOffset(new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", true, false, "Indomie Goreng (1 dus)", 115000m, 500, null, null },
                    { new Guid("a1000000-0000-4000-8000-000000000003"), new DateTimeOffset(new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", true, false, "Aqua 600ml (1 dus)", 52000m, 300, null, null },
                    { new Guid("a1000000-0000-4000-8000-000000000004"), new DateTimeOffset(new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", true, false, "Beras Premium 5kg", 78500m, 120, null, null },
                    { new Guid("a1000000-0000-4000-8000-000000000005"), new DateTimeOffset(new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", true, false, "Minyak Goreng 2L", 38000m, 200, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "UX_mProduct_txtName",
                table: "mProduct",
                column: "txtName",
                unique: true,
                filter: "\"bolDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "UX_mUser_txtUsername",
                table: "mUser",
                column: "txtUsername",
                unique: true,
                filter: "\"bolDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_trIdempotencyKey_dtInserted",
                table: "trIdempotencyKey",
                column: "dtInserted");

            migrationBuilder.CreateIndex(
                name: "UX_trIdempotencyKey_txtCustomerId_txtIdempotencyKey",
                table: "trIdempotencyKey",
                columns: new[] { "txtCustomerId", "txtIdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_trIdempotencyKey_uidOrderUUID",
                table: "trIdempotencyKey",
                column: "uidOrderUUID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trOrder_dtInserted",
                table: "trOrder",
                column: "dtInserted");

            migrationBuilder.CreateIndex(
                name: "IX_trOrder_txtCustomerId_dtInserted",
                table: "trOrder",
                columns: new[] { "txtCustomerId", "dtInserted" });

            migrationBuilder.CreateIndex(
                name: "IX_trOrder_txtStatus_dtInserted",
                table: "trOrder",
                columns: new[] { "txtStatus", "dtInserted" });

            migrationBuilder.CreateIndex(
                name: "IX_trOrderItem_uidProductUUID",
                table: "trOrderItem",
                column: "uidProductUUID");

            migrationBuilder.CreateIndex(
                name: "UX_trOrderItem_uidOrderUUID_uidProductUUID",
                table: "trOrderItem",
                columns: new[] { "uidOrderUUID", "uidProductUUID" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mUser");

            migrationBuilder.DropTable(
                name: "trIdempotencyKey");

            migrationBuilder.DropTable(
                name: "trOrderItem");

            migrationBuilder.DropTable(
                name: "mProduct");

            migrationBuilder.DropTable(
                name: "trOrder");
        }
    }
}
