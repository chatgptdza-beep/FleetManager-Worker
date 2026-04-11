using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FleetManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase1_Postgres_Proxies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NodeCapabilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VpsNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CanStartBrowser = table.Column<bool>(type: "boolean", nullable: false),
                    CanStopBrowser = table.Column<bool>(type: "boolean", nullable: false),
                    CanRestartBrowser = table.Column<bool>(type: "boolean", nullable: false),
                    CanCaptureScreenshot = table.Column<bool>(type: "boolean", nullable: false),
                    CanFetchLogs = table.Column<bool>(type: "boolean", nullable: false),
                    CanUpdateAgent = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NodeCapabilities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VpsNodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SshPort = table.Column<int>(type: "integer", nullable: false),
                    SshUsername = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AuthType = table.Column<string>(type: "text", nullable: false),
                    OsType = table.Column<string>(type: "text", nullable: false),
                    Region = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    LastHeartbeatAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CpuPercent = table.Column<double>(type: "double precision", nullable: false),
                    RamPercent = table.Column<double>(type: "double precision", nullable: false),
                    DiskPercent = table.Column<double>(type: "double precision", nullable: false),
                    RamUsedGb = table.Column<double>(type: "double precision", nullable: false),
                    StorageUsedGb = table.Column<double>(type: "double precision", nullable: false),
                    PingMs = table.Column<int>(type: "integer", nullable: false),
                    ActiveSessions = table.Column<int>(type: "integer", nullable: false),
                    ControlPort = table.Column<int>(type: "integer", nullable: false),
                    ConnectionState = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ConnectionTimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    AgentVersion = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VpsNodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    VpsNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentStageCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CurrentStageName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastStageTransitionAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CurrentProxyIndex = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Accounts_VpsNodes_VpsNodeId",
                        column: x => x.VpsNodeId,
                        principalTable: "VpsNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgentInstallJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VpsNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobStatus = table.Column<string>(type: "text", nullable: false),
                    CurrentStep = table.Column<string>(type: "text", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentInstallJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentInstallJobs_VpsNodes_VpsNodeId",
                        column: x => x.VpsNodeId,
                        principalTable: "VpsNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NodeCommands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VpsNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommandType = table.Column<string>(type: "text", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ResultMessage = table.Column<string>(type: "text", nullable: true),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExecutedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NodeCommands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NodeCommands_VpsNodes_VpsNodeId",
                        column: x => x.VpsNodeId,
                        principalTable: "VpsNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AccountAlerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    StageCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StageName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Severity = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    AcknowledgedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountAlerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountAlerts_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AccountWorkflowStages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    StageCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StageName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountWorkflowStages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountWorkflowStages_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProxyEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Host = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    Username = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Password = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    FailCount = table.Column<int>(type: "integer", nullable: false),
                    IsBlacklisted = table.Column<bool>(type: "boolean", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProxyEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProxyEntries_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProxyRotationLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromOrder = table.Column<int>(type: "integer", nullable: false),
                    ToOrder = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    RotatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProxyRotationLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProxyRotationLogs_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountAlerts_AccountId",
                table: "AccountAlerts",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_VpsNodeId",
                table: "Accounts",
                column: "VpsNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountWorkflowStages_AccountId",
                table: "AccountWorkflowStages",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentInstallJobs_VpsNodeId",
                table: "AgentInstallJobs",
                column: "VpsNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_NodeCommands_VpsNodeId",
                table: "NodeCommands",
                column: "VpsNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProxyEntries_AccountId",
                table: "ProxyEntries",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ProxyRotationLogs_AccountId",
                table: "ProxyRotationLogs",
                column: "AccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountAlerts");

            migrationBuilder.DropTable(
                name: "AccountWorkflowStages");

            migrationBuilder.DropTable(
                name: "AgentInstallJobs");

            migrationBuilder.DropTable(
                name: "NodeCapabilities");

            migrationBuilder.DropTable(
                name: "NodeCommands");

            migrationBuilder.DropTable(
                name: "ProxyEntries");

            migrationBuilder.DropTable(
                name: "ProxyRotationLogs");

            migrationBuilder.DropTable(
                name: "Accounts");

            migrationBuilder.DropTable(
                name: "VpsNodes");
        }
    }
}
