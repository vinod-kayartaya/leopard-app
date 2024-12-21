using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace admin_web.Migrations
{
    /// <inheritdoc />
    public partial class AddCertificateId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CertificateId",
                table: "Employees",
                type: "nvarchar(36)",
                maxLength: 36,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CertificateId",
                table: "Employees");
        }
    }
}
