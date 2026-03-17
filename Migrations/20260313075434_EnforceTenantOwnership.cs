using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace vizora.Migrations
{
    /// <inheritdoc />
    public partial class EnforceTenantOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    transaction_violation_count integer;
                    budget_violation_count integer;
                    recurring_violation_count integer;
                    violation_sample text;
                BEGIN
                    SELECT COUNT(*)
                    INTO transaction_violation_count
                    FROM "Transactions" t
                    INNER JOIN "Categories" c ON c."Id" = t."CategoryId"
                    WHERE t."UserId" <> c."UserId";

                    IF transaction_violation_count > 0 THEN
                        SELECT string_agg(
                            format('TransactionId=%s,TxUserId=%s,CategoryId=%s,CategoryUserId=%s',
                                v."Id",
                                v."UserId",
                                v."CategoryId",
                                v."CategoryUserId"),
                            ' | ')
                        INTO violation_sample
                        FROM (
                            SELECT t."Id", t."UserId", t."CategoryId", c."UserId" AS "CategoryUserId"
                            FROM "Transactions" t
                            INNER JOIN "Categories" c ON c."Id" = t."CategoryId"
                            WHERE t."UserId" <> c."UserId"
                            ORDER BY t."Id"
                            LIMIT 10
                        ) v;

                        RAISE EXCEPTION 'EnforceTenantOwnership blocked: Transactions contain % cross-user category reference(s). Sample: %',
                            transaction_violation_count,
                            COALESCE(violation_sample, 'n/a');
                    END IF;

                    SELECT COUNT(*)
                    INTO budget_violation_count
                    FROM "Budgets" b
                    INNER JOIN "Categories" c ON c."Id" = b."CategoryId"
                    WHERE b."UserId" <> c."UserId";

                    IF budget_violation_count > 0 THEN
                        SELECT string_agg(
                            format('BudgetId=%s,BudgetUserId=%s,CategoryId=%s,CategoryUserId=%s',
                                v."Id",
                                v."UserId",
                                v."CategoryId",
                                v."CategoryUserId"),
                            ' | ')
                        INTO violation_sample
                        FROM (
                            SELECT b."Id", b."UserId", b."CategoryId", c."UserId" AS "CategoryUserId"
                            FROM "Budgets" b
                            INNER JOIN "Categories" c ON c."Id" = b."CategoryId"
                            WHERE b."UserId" <> c."UserId"
                            ORDER BY b."Id"
                            LIMIT 10
                        ) v;

                        RAISE EXCEPTION 'EnforceTenantOwnership blocked: Budgets contain % cross-user category reference(s). Sample: %',
                            budget_violation_count,
                            COALESCE(violation_sample, 'n/a');
                    END IF;

                    SELECT COUNT(*)
                    INTO recurring_violation_count
                    FROM "RecurringTransactions" rt
                    INNER JOIN "Categories" c ON c."Id" = rt."CategoryId"
                    WHERE rt."UserId" <> c."UserId";

                    IF recurring_violation_count > 0 THEN
                        SELECT string_agg(
                            format('RecurringTransactionId=%s,RecurringUserId=%s,CategoryId=%s,CategoryUserId=%s',
                                v."Id",
                                v."UserId",
                                v."CategoryId",
                                v."CategoryUserId"),
                            ' | ')
                        INTO violation_sample
                        FROM (
                            SELECT rt."Id", rt."UserId", rt."CategoryId", c."UserId" AS "CategoryUserId"
                            FROM "RecurringTransactions" rt
                            INNER JOIN "Categories" c ON c."Id" = rt."CategoryId"
                            WHERE rt."UserId" <> c."UserId"
                            ORDER BY rt."Id"
                            LIMIT 10
                        ) v;

                        RAISE EXCEPTION 'EnforceTenantOwnership blocked: RecurringTransactions contain % cross-user category reference(s). Sample: %',
                            recurring_violation_count,
                            COALESCE(violation_sample, 'n/a');
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Budgets_Categories_CategoryId",
                table: "Budgets");

            migrationBuilder.DropForeignKey(
                name: "FK_RecurringTransactions_Categories_CategoryId",
                table: "RecurringTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Categories_CategoryId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_CategoryId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_RecurringTransactions_CategoryId",
                table: "RecurringTransactions");

            migrationBuilder.DropIndex(
                name: "IX_Budgets_CategoryId",
                table: "Budgets");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Categories_Id_UserId",
                table: "Categories",
                columns: new[] { "Id", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_CategoryId_UserId",
                table: "Transactions",
                columns: new[] { "CategoryId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_UserId",
                table: "Transactions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTransactions_CategoryId_UserId",
                table: "RecurringTransactions",
                columns: new[] { "CategoryId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTransactions_UserId",
                table: "RecurringTransactions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_UserId",
                table: "Categories",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_CategoryId_UserId",
                table: "Budgets",
                columns: new[] { "CategoryId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_UserId",
                table: "Budgets",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Budgets_Categories_CategoryId_UserId",
                table: "Budgets",
                columns: new[] { "CategoryId", "UserId" },
                principalTable: "Categories",
                principalColumns: new[] { "Id", "UserId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RecurringTransactions_Categories_CategoryId_UserId",
                table: "RecurringTransactions",
                columns: new[] { "CategoryId", "UserId" },
                principalTable: "Categories",
                principalColumns: new[] { "Id", "UserId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Categories_CategoryId_UserId",
                table: "Transactions",
                columns: new[] { "CategoryId", "UserId" },
                principalTable: "Categories",
                principalColumns: new[] { "Id", "UserId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Budgets_Categories_CategoryId_UserId",
                table: "Budgets");

            migrationBuilder.DropForeignKey(
                name: "FK_RecurringTransactions_Categories_CategoryId_UserId",
                table: "RecurringTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Categories_CategoryId_UserId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_CategoryId_UserId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_UserId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_RecurringTransactions_CategoryId_UserId",
                table: "RecurringTransactions");

            migrationBuilder.DropIndex(
                name: "IX_RecurringTransactions_UserId",
                table: "RecurringTransactions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Categories_Id_UserId",
                table: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_Categories_UserId",
                table: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_Budgets_CategoryId_UserId",
                table: "Budgets");

            migrationBuilder.DropIndex(
                name: "IX_Budgets_UserId",
                table: "Budgets");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_CategoryId",
                table: "Transactions",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTransactions_CategoryId",
                table: "RecurringTransactions",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_CategoryId",
                table: "Budgets",
                column: "CategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_Budgets_Categories_CategoryId",
                table: "Budgets",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RecurringTransactions_Categories_CategoryId",
                table: "RecurringTransactions",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Categories_CategoryId",
                table: "Transactions",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
