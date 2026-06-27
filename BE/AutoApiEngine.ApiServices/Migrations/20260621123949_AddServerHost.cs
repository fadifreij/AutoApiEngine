using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoApiEngine.ApiServices.Migrations
{
    /// <inheritdoc />
    public partial class AddServerHost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ServerHost",
                table: "Workspaces",
                type: "VARCHAR(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ServerHost",
                table: "Workspaces");
        }
    }
}
