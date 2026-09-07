using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prime.Infrastructure.Persistence.Migrations;

[Migration("20260901160000_AddEmployeeServiceIndicatorPonderation")]
public partial class AddEmployeeServiceIndicatorPonderation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "prime_employee_service_indicator_ponderation",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                EmployeeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                ServicePrimeIndicatorId = table.Column<Guid>(type: "uuid", nullable: false),
                PonderationPrimePct = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                PonderationChallengePct = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_prime_employee_service_indicator_ponderation", x => x.Id);
                table.ForeignKey(
                    name: "FK_prime_employee_service_indicator_ponderation_indicator",
                    column: x => x.ServicePrimeIndicatorId,
                    principalTable: "prime_service_prime_indicator",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_prime_employee_service_indicator_ponderation_employee_ind",
            table: "prime_employee_service_indicator_ponderation",
            columns: new[] { "EmployeeId", "ServicePrimeIndicatorId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_prime_employee_service_indicator_ponderation_indicator",
            table: "prime_employee_service_indicator_ponderation",
            column: "ServicePrimeIndicatorId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "prime_employee_service_indicator_ponderation");
    }
}
