using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SharpAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase7Observability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                table: "ToolExecutions",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "corr_legacy");

            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                table: "AuditEvents",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "corr_legacy");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "ToolExecutions");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "AuditEvents");
        }
    }
}
