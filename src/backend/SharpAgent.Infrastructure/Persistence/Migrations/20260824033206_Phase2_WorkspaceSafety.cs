using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SharpAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase2WorkspaceSafety : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AfterContentText",
                table: "FileChanges",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestJson",
                table: "ApprovalRequests",
                type: "TEXT",
                maxLength: 32000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SessionId",
                table: "ApprovalRequests",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WorktreePath",
                table: "AgentRuns",
                type: "TEXT",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_SessionId_Status",
                table: "ApprovalRequests",
                columns: new[] { "SessionId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ApprovalRequests_SessionId_Status",
                table: "ApprovalRequests");

            migrationBuilder.DropColumn(
                name: "AfterContentText",
                table: "FileChanges");

            migrationBuilder.DropColumn(
                name: "RequestJson",
                table: "ApprovalRequests");

            migrationBuilder.DropColumn(
                name: "SessionId",
                table: "ApprovalRequests");

            migrationBuilder.DropColumn(
                name: "WorktreePath",
                table: "AgentRuns");
        }
    }
}

