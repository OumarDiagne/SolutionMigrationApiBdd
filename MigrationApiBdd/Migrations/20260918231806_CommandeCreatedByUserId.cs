using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MigrationApiBdd.Migrations
{
    /// <inheritdoc />
    public partial class CommandeCreatedByUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "Commandes",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Commandes");
        }
    }
}
