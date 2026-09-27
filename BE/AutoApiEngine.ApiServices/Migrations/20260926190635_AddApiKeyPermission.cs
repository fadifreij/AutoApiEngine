using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoApiEngine.ApiServices.Migrations
{
    /// <inheritdoc />
    public partial class AddApiKeyPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApiKeyPermissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ApiKeyId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ObjectName = table.Column<string>(type: "VARCHAR(128)", maxLength: 128, nullable: true),
                    Verb = table.Column<string>(type: "VARCHAR(32)", maxLength: 32, nullable: true),
                    WorkspaceId = table.Column<Guid>(type: "char(36)", nullable: false),
                    DatabaseName = table.Column<string>(type: "VARCHAR(128)", maxLength: 128, nullable: false),
                    IsDeny = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeyPermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiKeyPermissions_ApiKeys_ApiKeyId",
                        column: x => x.ApiKeyId,
                        principalTable: "ApiKeys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeyPermissions_ApiKeyId_ObjectName_Verb_WorkspaceId_Datab~",
                table: "ApiKeyPermissions",
                columns: new[] { "ApiKeyId", "ObjectName", "Verb", "WorkspaceId", "DatabaseName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiKeyPermissions");
        }
    }
}
