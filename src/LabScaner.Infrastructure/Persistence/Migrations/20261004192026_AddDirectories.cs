using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LabScaner.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddDirectories : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "groups",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                name_key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                admission_year = table.Column<int>(type: "integer", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_groups", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "terms",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                academic_year = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                season = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                credit_week_start = table.Column<DateOnly>(type: "date", nullable: false),
                session_start = table.Column<DateOnly>(type: "date", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_terms", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "students",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                group_id = table.Column<int>(type: "integer", nullable: false),
                last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                middle_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                is_active = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_students", x => x.id);
                table.ForeignKey(
                    name: "fk_students_groups_group_id",
                    column: x => x.group_id,
                    principalTable: "groups",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "student_emails",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                student_id = table.Column<int>(type: "integer", nullable: false),
                email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_student_emails", x => x.id);
                table.ForeignKey(
                    name: "fk_student_emails_students_student_id",
                    column: x => x.student_id,
                    principalTable: "students",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_groups_name_key",
            table: "groups",
            column: "name_key",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_student_emails_email",
            table: "student_emails",
            column: "email");

        migrationBuilder.CreateIndex(
            name: "ix_student_emails_student_id_email",
            table: "student_emails",
            columns: new[] { "student_id", "email" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_students_group_id_last_name_first_name",
            table: "students",
            columns: new[] { "group_id", "last_name", "first_name" });

        migrationBuilder.CreateIndex(
            name: "ix_terms_academic_year_season",
            table: "terms",
            columns: new[] { "academic_year", "season" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "student_emails");

        migrationBuilder.DropTable(
            name: "terms");

        migrationBuilder.DropTable(
            name: "students");

        migrationBuilder.DropTable(
            name: "groups");
    }
}
