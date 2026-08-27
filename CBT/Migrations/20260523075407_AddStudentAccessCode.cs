using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NCS.CBT.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentAccessCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccessCodeHash",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccessCodeHash",
                table: "AspNetUsers");
        }
    }
}
