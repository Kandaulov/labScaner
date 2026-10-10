using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LabScaner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssignmentTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "general_requirements",
                table: "subject_terms",
                type: "character varying(20000)",
                maxLength: 20000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<List<string>>(
                name: "checklist",
                table: "assignments",
                type: "text[]",
                nullable: false);

            migrationBuilder.AddColumn<string>(
                name: "task_text",
                table: "assignments",
                type: "character varying(30000)",
                maxLength: 30000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "task_documents",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    teacher_id = table.Column<int>(type: "integer", nullable: false),
                    subject_term_id = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_documents_subject_terms_subject_term_id",
                        column: x => x.subject_term_id,
                        principalTable: "subject_terms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_task_documents_subject_term_id_kind",
                table: "task_documents",
                columns: new[] { "subject_term_id", "kind" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "task_documents");

            migrationBuilder.DropColumn(
                name: "general_requirements",
                table: "subject_terms");

            migrationBuilder.DropColumn(
                name: "checklist",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "task_text",
                table: "assignments");
        }
    }
}
