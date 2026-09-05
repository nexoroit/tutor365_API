using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tutor365.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AssessmentsAndPlanItemTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TimeLimitMinutes",
                table: "StudySessions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ItemType",
                table: "StudyPlanItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AssessmentAttempts",
                table: "StudentTopicProgress",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "AssessmentPassedAt",
                table: "StudentTopicProgress",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LastAssessmentPercent",
                table: "StudentTopicProgress",
                type: "decimal(18,4)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TimeLimitMinutes",
                table: "StudySessions");

            migrationBuilder.DropColumn(
                name: "ItemType",
                table: "StudyPlanItems");

            migrationBuilder.DropColumn(
                name: "AssessmentAttempts",
                table: "StudentTopicProgress");

            migrationBuilder.DropColumn(
                name: "AssessmentPassedAt",
                table: "StudentTopicProgress");

            migrationBuilder.DropColumn(
                name: "LastAssessmentPercent",
                table: "StudentTopicProgress");
        }
    }
}
