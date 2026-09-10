using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tutor365.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class WeekdaySchedulesAndHelpOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DayPlansJson",
                table: "StudySchedules",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowExamples",
                table: "Students",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowExplainDifferently",
                table: "Students",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowHints",
                table: "Students",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DayPlansJson",
                table: "StudySchedules");

            migrationBuilder.DropColumn(
                name: "AllowExamples",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "AllowExplainDifferently",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "AllowHints",
                table: "Students");
        }
    }
}
