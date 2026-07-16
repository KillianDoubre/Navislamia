using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Navislamia.Game.DataAccess.Migrations.Arcadia
{
    /// <inheritdoc />
    public partial class ReshapeItemResourceEffectValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ItemResourceEntity_BaseValues_MaxSize8",
                table: "ItemResources");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ItemResourceEntity_OptValues_MaxSize8",
                table: "ItemResources");

            migrationBuilder.RenameColumn(
                name: "OptValues",
                table: "ItemResources",
                newName: "OptVar2");

            migrationBuilder.RenameColumn(
                name: "BaseValues",
                table: "ItemResources",
                newName: "OptVar1");

            migrationBuilder.AddColumn<decimal[]>(
                name: "BaseVar1",
                table: "ItemResources",
                type: "numeric[]",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal[]>(
                name: "BaseVar2",
                table: "ItemResources",
                type: "numeric[]",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ItemResourceEntity_BaseVar1_MaxSize4",
                table: "ItemResources",
                sql: "cardinality(\"BaseVar1\") <= 4");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ItemResourceEntity_BaseVar2_MaxSize4",
                table: "ItemResources",
                sql: "cardinality(\"BaseVar2\") <= 4");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ItemResourceEntity_OptVar1_MaxSize4",
                table: "ItemResources",
                sql: "cardinality(\"OptVar1\") <= 4");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ItemResourceEntity_OptVar2_MaxSize4",
                table: "ItemResources",
                sql: "cardinality(\"OptVar2\") <= 4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ItemResourceEntity_BaseVar1_MaxSize4",
                table: "ItemResources");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ItemResourceEntity_BaseVar2_MaxSize4",
                table: "ItemResources");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ItemResourceEntity_OptVar1_MaxSize4",
                table: "ItemResources");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ItemResourceEntity_OptVar2_MaxSize4",
                table: "ItemResources");

            migrationBuilder.DropColumn(
                name: "BaseVar1",
                table: "ItemResources");

            migrationBuilder.DropColumn(
                name: "BaseVar2",
                table: "ItemResources");

            migrationBuilder.RenameColumn(
                name: "OptVar2",
                table: "ItemResources",
                newName: "OptValues");

            migrationBuilder.RenameColumn(
                name: "OptVar1",
                table: "ItemResources",
                newName: "BaseValues");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ItemResourceEntity_BaseValues_MaxSize8",
                table: "ItemResources",
                sql: "cardinality(\"BaseValues\") <= 8");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ItemResourceEntity_OptValues_MaxSize8",
                table: "ItemResources",
                sql: "cardinality(\"OptValues\") <= 8");
        }
    }
}
