using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NCS.CBT.Migrations
{
    /// <inheritdoc />
    public partial class AddProctoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDisqualified",
                table: "ExamSessions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ViolationCount",
                table: "ExamSessions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ProctorViolations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExamSessionId = table.Column<int>(type: "int", nullable: false),
                    ViolationType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SnapshotPath = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProctorViolations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProctorViolations_ExamSessions_ExamSessionId",
                        column: x => x.ExamSessionId,
                        principalTable: "ExamSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProctorViolations_ExamSessionId",
                table: "ProctorViolations",
                column: "ExamSessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProctorViolations");

            migrationBuilder.DropColumn(
                name: "IsDisqualified",
                table: "ExamSessions");

            migrationBuilder.DropColumn(
                name: "ViolationCount",
                table: "ExamSessions");
        }
    }
}
