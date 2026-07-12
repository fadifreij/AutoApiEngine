using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoApiEngine.ApiServices.Migrations
{
    /// <inheritdoc />
    public partial class AddDeployedApi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeployedApis",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Name = table.Column<string>(type: "VARCHAR(250)", maxLength: 250, nullable: false),
                    ObjectName = table.Column<string>(type: "VARCHAR(250)", maxLength: 250, nullable: false),
                    SelectColumns = table.Column<string>(type: "longtext", nullable: true),
                    Filters = table.Column<string>(type: "longtext", nullable: true),
                    Sorts = table.Column<string>(type: "longtext", nullable: true),
                    PageSize = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeployedApis", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeployedApis_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_DeployedApis_WorkspaceId",
                table: "DeployedApis",
                column: "WorkspaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeployedApis");
        }
    }
}
