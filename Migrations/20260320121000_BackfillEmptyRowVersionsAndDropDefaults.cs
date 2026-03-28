using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Vizora.Data;

#nullable disable

namespace vizora.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260320121000_BackfillEmptyRowVersionsAndDropDefaults")]
    public partial class BackfillEmptyRowVersionsAndDropDefaults : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "Transactions",
                type: "bytea",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldDefaultValue: new byte[0]);

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "RecurringTransactions",
                type: "bytea",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldDefaultValue: new byte[0]);

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "Categories",
                type: "bytea",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldDefaultValue: new byte[0]);

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "Budgets",
                type: "bytea",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldDefaultValue: new byte[0]);

            migrationBuilder.Sql(
                """
                UPDATE "Categories"
                SET "RowVersion" = decode(md5(random()::text || clock_timestamp()::text || "Id"::text), 'hex')
                WHERE "RowVersion" IS NULL OR octet_length("RowVersion") = 0;

                UPDATE "Transactions"
                SET "RowVersion" = decode(md5(random()::text || clock_timestamp()::text || "Id"::text), 'hex')
                WHERE "RowVersion" IS NULL OR octet_length("RowVersion") = 0;

                UPDATE "Budgets"
                SET "RowVersion" = decode(md5(random()::text || clock_timestamp()::text || "Id"::text), 'hex')
                WHERE "RowVersion" IS NULL OR octet_length("RowVersion") = 0;

                UPDATE "RecurringTransactions"
                SET "RowVersion" = decode(md5(random()::text || clock_timestamp()::text || "Id"::text), 'hex')
                WHERE "RowVersion" IS NULL OR octet_length("RowVersion") = 0;

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "Categories" WHERE "RowVersion" IS NULL OR octet_length("RowVersion") = 0) THEN
                        RAISE EXCEPTION 'Backfill failed: Categories.RowVersion still contains empty values.';
                    END IF;

                    IF EXISTS (SELECT 1 FROM "Transactions" WHERE "RowVersion" IS NULL OR octet_length("RowVersion") = 0) THEN
                        RAISE EXCEPTION 'Backfill failed: Transactions.RowVersion still contains empty values.';
                    END IF;

                    IF EXISTS (SELECT 1 FROM "Budgets" WHERE "RowVersion" IS NULL OR octet_length("RowVersion") = 0) THEN
                        RAISE EXCEPTION 'Backfill failed: Budgets.RowVersion still contains empty values.';
                    END IF;

                    IF EXISTS (SELECT 1 FROM "RecurringTransactions" WHERE "RowVersion" IS NULL OR octet_length("RowVersion") = 0) THEN
                        RAISE EXCEPTION 'Backfill failed: RecurringTransactions.RowVersion still contains empty values.';
                    END IF;
                END $$;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "Transactions",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0],
                oldClrType: typeof(byte[]),
                oldType: "bytea");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "RecurringTransactions",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0],
                oldClrType: typeof(byte[]),
                oldType: "bytea");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "Categories",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0],
                oldClrType: typeof(byte[]),
                oldType: "bytea");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "Budgets",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0],
                oldClrType: typeof(byte[]),
                oldType: "bytea");
        }
    }
}
