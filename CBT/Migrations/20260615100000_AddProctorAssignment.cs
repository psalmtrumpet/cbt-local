using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NCS.CBT.Migrations
{
    /// <inheritdoc />
    public partial class AddProctorAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssignedProctorId",
                table: "ExamSessions",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssignedProctorId",
                table: "ExamSessions");
        }
    }
}
