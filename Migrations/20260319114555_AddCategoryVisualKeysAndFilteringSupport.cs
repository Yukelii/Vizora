using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace vizora.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoryVisualKeysAndFilteringSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ColorKey",
                table: "Categories",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "slate");

            migrationBuilder.AddColumn<string>(
                name: "IconKey",
                table: "Categories",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "receipt_long");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ColorKey",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "IconKey",
                table: "Categories");
        }
    }
}
