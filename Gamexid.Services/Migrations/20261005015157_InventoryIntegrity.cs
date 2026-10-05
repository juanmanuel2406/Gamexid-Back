using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gamexid.Services.Migrations
{
    /// <inheritdoc />
    public partial class InventoryIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Users_BranchId",
                table: "Users",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_SerializedUnits_CurrentBranchId",
                table: "SerializedUnits",
                column: "CurrentBranchId");

            migrationBuilder.CreateIndex(
                name: "IX_SerializedUnits_ProductId",
                table: "SerializedUnits",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_DestinationBranchId",
                table: "InventoryMovements",
                column: "DestinationBranchId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_RegisteredByUserId",
                table: "InventoryMovements",
                column: "RegisteredByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_SourceBranchId",
                table: "InventoryMovements",
                column: "SourceBranchId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovementItems_ProductId",
                table: "InventoryMovementItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovementItems_SerializedUnitId",
                table: "InventoryMovementItems",
                column: "SerializedUnitId");

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryMovementItems_Products_ProductId",
                table: "InventoryMovementItems",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryMovementItems_SerializedUnits_SerializedUnitId",
                table: "InventoryMovementItems",
                column: "SerializedUnitId",
                principalTable: "SerializedUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryMovements_Branches_DestinationBranchId",
                table: "InventoryMovements",
                column: "DestinationBranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryMovements_Branches_SourceBranchId",
                table: "InventoryMovements",
                column: "SourceBranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryMovements_Users_RegisteredByUserId",
                table: "InventoryMovements",
                column: "RegisteredByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SerializedUnits_Branches_CurrentBranchId",
                table: "SerializedUnits",
                column: "CurrentBranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SerializedUnits_Products_ProductId",
                table: "SerializedUnits",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Branches_BranchId",
                table: "Users",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryMovementItems_Products_ProductId",
                table: "InventoryMovementItems");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryMovementItems_SerializedUnits_SerializedUnitId",
                table: "InventoryMovementItems");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryMovements_Branches_DestinationBranchId",
                table: "InventoryMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryMovements_Branches_SourceBranchId",
                table: "InventoryMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryMovements_Users_RegisteredByUserId",
                table: "InventoryMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_SerializedUnits_Branches_CurrentBranchId",
                table: "SerializedUnits");

            migrationBuilder.DropForeignKey(
                name: "FK_SerializedUnits_Products_ProductId",
                table: "SerializedUnits");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Branches_BranchId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_BranchId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_SerializedUnits_CurrentBranchId",
                table: "SerializedUnits");

            migrationBuilder.DropIndex(
                name: "IX_SerializedUnits_ProductId",
                table: "SerializedUnits");

            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_DestinationBranchId",
                table: "InventoryMovements");

            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_RegisteredByUserId",
                table: "InventoryMovements");

            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_SourceBranchId",
                table: "InventoryMovements");

            migrationBuilder.DropIndex(
                name: "IX_InventoryMovementItems_ProductId",
                table: "InventoryMovementItems");

            migrationBuilder.DropIndex(
                name: "IX_InventoryMovementItems_SerializedUnitId",
                table: "InventoryMovementItems");
        }
    }
}
