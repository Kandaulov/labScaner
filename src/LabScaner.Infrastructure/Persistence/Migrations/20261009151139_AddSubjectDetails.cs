using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabScaner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubjectDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ai_reference_text",
                table: "subjects",
                type: "character varying(20000)",
                maxLength: 20000,
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "aliases",
                table: "subjects",
                type: "text[]",
                nullable: false);

            migrationBuilder.AddColumn<string>(
                name: "disk_path_template",
                table: "subjects",
                type: "character varying(400)",
                maxLength: 400,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "final_assessment",
                table: "subjects",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ai_reference_text",
                table: "subjects");

            migrationBuilder.DropColumn(
                name: "aliases",
                table: "subjects");

            migrationBuilder.DropColumn(
                name: "disk_path_template",
                table: "subjects");

            migrationBuilder.DropColumn(
                name: "final_assessment",
                table: "subjects");
        }
    }
}
