using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LabScaner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDiskConnection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "disk_connections",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    teacher_id = table.Column<int>(type: "integer", nullable: false),
                    access_token_protected = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    refresh_token_protected = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    connected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    login = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    total_space = table.Column<long>(type: "bigint", nullable: true),
                    used_space = table.Column<long>(type: "bigint", nullable: true),
                    last_check_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_check_ok = table.Column<bool>(type: "boolean", nullable: true),
                    last_check_message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_disk_connections", x => x.id);
                    table.ForeignKey(
                        name: "fk_disk_connections_users_teacher_id",
                        column: x => x.teacher_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_disk_connections_teacher_id",
                table: "disk_connections",
                column: "teacher_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "disk_connections");
        }
    }
}
