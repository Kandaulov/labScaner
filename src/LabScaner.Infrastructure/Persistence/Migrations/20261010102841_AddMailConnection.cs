using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LabScaner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMailConnection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mail_connections",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    teacher_id = table.Column<int>(type: "integer", nullable: false),
                    address = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    login = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    password_protected = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    imap_host = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    imap_port = table.Column<int>(type: "integer", nullable: false),
                    imap_security = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    smtp_host = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    smtp_port = table.Column<int>(type: "integer", nullable: false),
                    smtp_security = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    read_since = table.Column<DateOnly>(type: "date", nullable: false),
                    poll_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    poll_interval_minutes = table.Column<int>(type: "integer", nullable: false),
                    imap_uid_validity = table.Column<long>(type: "bigint", nullable: true),
                    imap_last_uid = table.Column<long>(type: "bigint", nullable: true),
                    last_check_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_check_ok = table.Column<bool>(type: "boolean", nullable: true),
                    last_check_message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mail_connections", x => x.id);
                    table.ForeignKey(
                        name: "fk_mail_connections_users_teacher_id",
                        column: x => x.teacher_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mail_connections_teacher_id",
                table: "mail_connections",
                column: "teacher_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mail_connections");
        }
    }
}
