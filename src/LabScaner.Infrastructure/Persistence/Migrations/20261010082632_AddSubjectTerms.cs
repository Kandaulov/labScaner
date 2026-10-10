using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LabScaner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubjectTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "subject_terms",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    teacher_id = table.Column<int>(type: "integer", nullable: false),
                    subject_id = table.Column<int>(type: "integer", nullable: false),
                    term_id = table.Column<int>(type: "integer", nullable: false),
                    study_semester = table.Column<int>(type: "integer", nullable: false),
                    disk_root_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subject_terms", x => x.id);
                    table.ForeignKey(
                        name: "fk_subject_terms_subjects_subject_id",
                        column: x => x.subject_id,
                        principalTable: "subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_subject_terms_terms_term_id",
                        column: x => x.term_id,
                        principalTable: "terms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "assignments",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    teacher_id = table.Column<int>(type: "integer", nullable: false),
                    subject_term_id = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ai_check_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    deadline_override = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_assignments_subject_terms_subject_term_id",
                        column: x => x.subject_term_id,
                        principalTable: "subject_terms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "coursework_topics",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    teacher_id = table.Column<int>(type: "integer", nullable: false),
                    subject_term_id = table.Column<int>(type: "integer", nullable: false),
                    student_id = table.Column<int>(type: "integer", nullable: false),
                    topic = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_coursework_topics", x => x.id);
                    table.ForeignKey(
                        name: "fk_coursework_topics_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_coursework_topics_subject_terms_subject_term_id",
                        column: x => x.subject_term_id,
                        principalTable: "subject_terms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subject_term_groups",
                columns: table => new
                {
                    group_id = table.Column<int>(type: "integer", nullable: false),
                    subject_term_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subject_term_groups", x => new { x.group_id, x.subject_term_id });
                    table.ForeignKey(
                        name: "fk_subject_term_groups_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_subject_term_groups_subject_terms_subject_term_id",
                        column: x => x.subject_term_id,
                        principalTable: "subject_terms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assignments_subject_term_id_kind_number",
                table: "assignments",
                columns: new[] { "subject_term_id", "kind", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_coursework_topics_student_id",
                table: "coursework_topics",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ix_coursework_topics_subject_term_id_student_id",
                table: "coursework_topics",
                columns: new[] { "subject_term_id", "student_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subject_term_groups_subject_term_id",
                table: "subject_term_groups",
                column: "subject_term_id");

            migrationBuilder.CreateIndex(
                name: "ix_subject_terms_subject_id_term_id",
                table: "subject_terms",
                columns: new[] { "subject_id", "term_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subject_terms_term_id",
                table: "subject_terms",
                column: "term_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assignments");

            migrationBuilder.DropTable(
                name: "coursework_topics");

            migrationBuilder.DropTable(
                name: "subject_term_groups");

            migrationBuilder.DropTable(
                name: "subject_terms");
        }
    }
}
