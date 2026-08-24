using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SharpAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApprovalRequests",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RunId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ActionFingerprint = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ActionType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    AffectedPathsJson = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Decision = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ExpiresAtUtc = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ResolvedAtUtc = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RunId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", maxLength: 32000, nullable: false),
                    OccurredAtUtc = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChangeSets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RunId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChangeSets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Operation = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RequestHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ResponseJson = table.Column<string>(type: "TEXT", maxLength: 32000, nullable: false),
                    StatusCode = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ExpiresAtUtc = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "ModelProfiles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ProviderModelId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EndpointKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CapabilitiesJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ValidationStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ConfigReference = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    ValidationMessage = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    UpdatedAtUtc = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PolicyProfiles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    RulesJson = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: false),
                    MaxRunDurationMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxToolCalls = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxEstimatedCostUsd = table.Column<decimal>(type: "TEXT", nullable: false),
                    ApprovalExpiryMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    UpdatedAtUtc = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RunLeases",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RunId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AcquiredAtUtc = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ReleasedAtUtc = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunLeases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    WorkspaceId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Task = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ModelProfileId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PolicyProfileId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ActiveRunId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ArchivedAtUtc = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    LastInstruction = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    LastEventSequence = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    UpdatedAtUtc = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TodoItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RunId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    UpdatedAtUtc = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TodoItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ToolExecutions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RunId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ToolName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RequestSummary = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    PolicyOutcome = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ApprovalId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    StartedAtUtc = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    EndedAtUtc = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    ExitCode = table.Column<int>(type: "INTEGER", nullable: true),
                    OutputPreview = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    OutputTruncated = table.Column<bool>(type: "INTEGER", nullable: false),
                    RedactionApplied = table.Column<bool>(type: "INTEGER", nullable: false),
                    ErrorSummary = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToolExecutions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UsageRecords",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RunId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ModelProfileId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    InputTokens = table.Column<long>(type: "INTEGER", nullable: true),
                    OutputTokens = table.Column<long>(type: "INTEGER", nullable: true),
                    EstimatedCostUsd = table.Column<decimal>(type: "TEXT", nullable: true),
                    LatencyMs = table.Column<long>(type: "INTEGER", nullable: true),
                    ContextCompactions = table.Column<int>(type: "INTEGER", nullable: false),
                    ToolCalls = table.Column<int>(type: "INTEGER", nullable: false),
                    RecordedAtUtc = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Workspaces",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    RootPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    CanonicalRootPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AllowedPathsJson = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    DefaultModelProfileId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ValidationMessage = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    UpdatedAtUtc = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workspaces", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FileChanges",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ChangeSetId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ChangeType = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    BeforeHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    AfterHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    DiffText = table.Column<string>(type: "TEXT", maxLength: 32000, nullable: true),
                    IsBinary = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileChanges_ChangeSets_ChangeSetId",
                        column: x => x.ChangeSetId,
                        principalTable: "ChangeSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgentRuns",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ResumeSourceRunId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ExecutionEnvironmentId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    StartedAtUtc = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    EndedAtUtc = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    CancelRequestedAtUtc = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    StopReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ContextSummary = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: true),
                    FinalSummary = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentRuns_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentRuns_SessionId",
                table: "AgentRuns",
                column: "SessionId",
                unique: true,
                filter: "\"Status\" IN ('Planning', 'Executing', 'AwaitingApproval', 'Reviewing')");

            migrationBuilder.CreateIndex(
                name: "IX_AgentRuns_SessionId_Sequence",
                table: "AgentRuns",
                columns: new[] { "SessionId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_ExpiresAtUtc",
                table: "ApprovalRequests",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_RunId_Status",
                table: "ApprovalRequests",
                columns: new[] { "RunId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_SessionId_Sequence",
                table: "AuditEvents",
                columns: new[] { "SessionId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChangeSets_RunId",
                table: "ChangeSets",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_FileChanges_ChangeSetId",
                table: "FileChanges",
                column: "ChangeSetId");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_ExpiresAtUtc",
                table: "IdempotencyRecords",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ModelProfiles_DisplayName",
                table: "ModelProfiles",
                column: "DisplayName");

            migrationBuilder.CreateIndex(
                name: "IX_RunLeases_RunId",
                table: "RunLeases",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_RunLeases_SessionId",
                table: "RunLeases",
                column: "SessionId",
                unique: true,
                filter: "\"ReleasedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_Status",
                table: "Sessions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_UpdatedAtUtc",
                table: "Sessions",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_WorkspaceId",
                table: "Sessions",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_TodoItems_RunId_Sequence",
                table: "TodoItems",
                columns: new[] { "RunId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TodoItems_SessionId",
                table: "TodoItems",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ToolExecutions_RunId",
                table: "ToolExecutions",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_UsageRecords_RunId",
                table: "UsageRecords",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_UsageRecords_SessionId",
                table: "UsageRecords",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Workspaces_Name",
                table: "Workspaces",
                column: "Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentRuns");

            migrationBuilder.DropTable(
                name: "ApprovalRequests");

            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "FileChanges");

            migrationBuilder.DropTable(
                name: "IdempotencyRecords");

            migrationBuilder.DropTable(
                name: "ModelProfiles");

            migrationBuilder.DropTable(
                name: "PolicyProfiles");

            migrationBuilder.DropTable(
                name: "RunLeases");

            migrationBuilder.DropTable(
                name: "TodoItems");

            migrationBuilder.DropTable(
                name: "ToolExecutions");

            migrationBuilder.DropTable(
                name: "UsageRecords");

            migrationBuilder.DropTable(
                name: "Workspaces");

            migrationBuilder.DropTable(
                name: "Sessions");

            migrationBuilder.DropTable(
                name: "ChangeSets");
        }
    }
}
