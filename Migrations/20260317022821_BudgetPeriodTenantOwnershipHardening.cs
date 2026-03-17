using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace vizora.Migrations
{
    /// <inheritdoc />
    public partial class BudgetPeriodTenantOwnershipHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    budget_period_violation_count integer;
                    violation_sample text;
                BEGIN
                    SELECT COUNT(*)
                    INTO budget_period_violation_count
                    FROM "Budgets" b
                    INNER JOIN "BudgetPeriods" bp ON bp."Id" = b."BudgetPeriodId"
                    WHERE b."UserId" <> bp."UserId";

                    IF budget_period_violation_count > 0 THEN
                        SELECT string_agg(
                            format('BudgetId=%s,BudgetUserId=%s,BudgetPeriodId=%s,BudgetPeriodUserId=%s',
                                v."Id",
                                v."UserId",
                                v."BudgetPeriodId",
                                v."BudgetPeriodUserId"),
                            ' | ')
                        INTO violation_sample
                        FROM (
                            SELECT b."Id", b."UserId", b."BudgetPeriodId", bp."UserId" AS "BudgetPeriodUserId"
                            FROM "Budgets" b
                            INNER JOIN "BudgetPeriods" bp ON bp."Id" = b."BudgetPeriodId"
                            WHERE b."UserId" <> bp."UserId"
                            ORDER BY b."Id"
                            LIMIT 10
                        ) v;

                        RAISE EXCEPTION 'BudgetPeriodTenantOwnershipHardening blocked: Budgets contain % cross-user budget-period reference(s). Sample: %',
                            budget_period_violation_count,
                            COALESCE(violation_sample, 'n/a');
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Budgets_BudgetPeriods_BudgetPeriodId",
                table: "Budgets");

            migrationBuilder.DropIndex(
                name: "IX_Budgets_BudgetPeriodId",
                table: "Budgets");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_BudgetPeriods_Id_UserId",
                table: "BudgetPeriods",
                columns: new[] { "Id", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_BudgetPeriodId_UserId",
                table: "Budgets",
                columns: new[] { "BudgetPeriodId", "UserId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Budgets_BudgetPeriods_BudgetPeriodId_UserId",
                table: "Budgets",
                columns: new[] { "BudgetPeriodId", "UserId" },
                principalTable: "BudgetPeriods",
                principalColumns: new[] { "Id", "UserId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Budgets_BudgetPeriods_BudgetPeriodId_UserId",
                table: "Budgets");

            migrationBuilder.DropIndex(
                name: "IX_Budgets_BudgetPeriodId_UserId",
                table: "Budgets");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_BudgetPeriods_Id_UserId",
                table: "BudgetPeriods");

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_BudgetPeriodId",
                table: "Budgets",
                column: "BudgetPeriodId");

            migrationBuilder.AddForeignKey(
                name: "FK_Budgets_BudgetPeriods_BudgetPeriodId",
                table: "Budgets",
                column: "BudgetPeriodId",
                principalTable: "BudgetPeriods",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
