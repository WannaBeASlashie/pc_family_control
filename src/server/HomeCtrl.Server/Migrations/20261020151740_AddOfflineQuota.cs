using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeCtrl.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddOfflineQuota : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClientRecordId",
                table: "Sessions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "OfflineQuotaSeconds",
                table: "Policies",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_ClientRecordId",
                table: "Sessions",
                column: "ClientRecordId",
                unique: true,
                filter: "[ClientRecordId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sessions_ClientRecordId",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "ClientRecordId",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "OfflineQuotaSeconds",
                table: "Policies");
        }
    }
}
