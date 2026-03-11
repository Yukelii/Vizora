using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace vizora.Migrations
{
    public partial class AddFinanceOwnershipForeignKeys : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                INSERT INTO "AspNetUsers" (
                    "Id",
                    "CreatedAt",
                    "AccessFailedCount",
                    "EmailConfirmed",
                    "LockoutEnabled",
                    "PhoneNumberConfirmed",
                    "TwoFactorEnabled"
                )
                SELECT missing_owner."UserId",
                       CURRENT_TIMESTAMP,
                       0,
                       FALSE,
                       FALSE,
                       FALSE,
                       FALSE
                FROM (
                    SELECT "UserId" FROM "Categories"
                    UNION
                    SELECT "UserId" FROM "Transactions"
                ) AS missing_owner
                LEFT JOIN "AspNetUsers" existing_user
                    ON existing_user."Id" = missing_owner."UserId"
                WHERE existing_user."Id" IS NULL;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_Categories_AspNetUsers_UserId",
                table: "Categories",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_AspNetUsers_UserId",
                table: "Transactions",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Categories_AspNetUsers_UserId",
                table: "Categories");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_AspNetUsers_UserId",
                table: "Transactions");
        }
    }
}
