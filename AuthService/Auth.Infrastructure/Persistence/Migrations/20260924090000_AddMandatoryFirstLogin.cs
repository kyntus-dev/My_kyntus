using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Auth.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AuthDbContext))]
[Migration("20260924090000_AddMandatoryFirstLogin")]
public class AddMandatoryFirstLogin : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>("MustChangePassword", "Users", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<int>("CredentialVersion", "Users", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<DateTime>("TemporaryPasswordExpiresAt", "Users", nullable: true);
        migrationBuilder.AddColumn<DateTime>("PasswordChangedAt", "Users", nullable: true);
        migrationBuilder.AddColumn<string>("FirstLoginTokenHash", "Users", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<DateTime>("FirstLoginTokenExpiresAt", "Users", nullable: true);
        migrationBuilder.CreateIndex("IX_Users_FirstLoginTokenHash", "Users", "FirstLoginTokenHash", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Migration de sécurité : conserver le schéma et les contrôles après activation des comptes.");
}
