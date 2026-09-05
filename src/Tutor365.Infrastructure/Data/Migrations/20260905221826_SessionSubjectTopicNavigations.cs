using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tutor365.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SessionSubjectTopicNavigations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_StudySessions_SubjectId",
                table: "StudySessions",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_StudySessions_TopicId",
                table: "StudySessions",
                column: "TopicId");

            migrationBuilder.AddForeignKey(
                name: "FK_StudySessions_Subjects_SubjectId",
                table: "StudySessions",
                column: "SubjectId",
                principalTable: "Subjects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StudySessions_Topics_TopicId",
                table: "StudySessions",
                column: "TopicId",
                principalTable: "Topics",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StudySessions_Subjects_SubjectId",
                table: "StudySessions");

            migrationBuilder.DropForeignKey(
                name: "FK_StudySessions_Topics_TopicId",
                table: "StudySessions");

            migrationBuilder.DropIndex(
                name: "IX_StudySessions_SubjectId",
                table: "StudySessions");

            migrationBuilder.DropIndex(
                name: "IX_StudySessions_TopicId",
                table: "StudySessions");
        }
    }
}
