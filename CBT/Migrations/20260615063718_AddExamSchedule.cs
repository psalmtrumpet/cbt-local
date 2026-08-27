using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NCS.CBT.Migrations
{
    /// <inheritdoc />
    public partial class AddExamSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledEnd",
                table: "Exams",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledStart",
                table: "Exams",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScheduledEnd",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "ScheduledStart",
                table: "Exams");
        }
    }
}
