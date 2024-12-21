using Microsoft.EntityFrameworkCore.Migrations;

public partial class UpdateCertificateIdLength : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "CertificateId",
            table: "Employees",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: true,
            oldType: "nvarchar(36)",
            oldMaxLength: 36,
            oldNullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "CertificateId",
            table: "Employees",
            type: "nvarchar(36)",
            maxLength: 36,
            nullable: true,
            oldType: "nvarchar(100)",
            oldMaxLength: 100,
            oldNullable: true);
    }
} 