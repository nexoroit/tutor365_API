using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tutor365.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class NumericAnswerPrecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "NumericValue",
                table: "QuestionAnswers",
                type: "decimal(28,10)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "NumericTolerance",
                table: "QuestionAnswers",
                type: "decimal(28,10)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "NumericValue",
                table: "QuestionAnswers",
                type: "decimal(18,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,10)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "NumericTolerance",
                table: "QuestionAnswers",
                type: "decimal(18,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,10)",
                oldNullable: true);
        }
    }
}
