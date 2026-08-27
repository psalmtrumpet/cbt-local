using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NCS.CBT.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentExamAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AssignedExamId",
                table: "AspNetUsers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_AssignedExamId",
                table: "AspNetUsers",
                column: "AssignedExamId");

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Exams_AssignedExamId",
                table: "AspNetUsers",
                column: "AssignedExamId",
                principalTable: "Exams",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Exams_AssignedExamId",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_AssignedExamId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "AssignedExamId",
                table: "AspNetUsers");
        }
    }
}
