using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WillBeyond.TimeBank.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSlidingWindowQuota : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "SlidingQuotaSeconds",
                table: "Policies",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "SlidingWindowSeconds",
                table: "Policies",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SlidingQuotaSeconds",
                table: "Policies");

            migrationBuilder.DropColumn(
                name: "SlidingWindowSeconds",
                table: "Policies");
        }
    }
}
