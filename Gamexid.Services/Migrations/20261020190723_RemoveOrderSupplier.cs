using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gamexid.Services.Migrations
{
    public partial class RemoveOrderSupplier : Migration
    {
            protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Orders_BranchId_Reference",
                table: "Orders",
                columns: new[] { "BranchId", "Reference" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_Orders_BranchId_Supplier_Reference",
                table: "Orders");

            migrationBuilder.DropColumn(name: "Supplier", table: "Orders");
        }

            protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_BranchId_Reference",
                table: "Orders");

            migrationBuilder.AddColumn<string>(
                name: "Supplier",
                table: "Orders",
                type: "varchar(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_BranchId_Supplier_Reference",
                table: "Orders",
                columns: new[] { "BranchId", "Supplier", "Reference" },
                unique: true);
        }
    }
}
