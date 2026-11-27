using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The schema of this version from an empty database, with the rows a fresh installation needs (the system providers and the
    /// placeholder of the site settings). It replaces every earlier migration; a database created by earlier migrations is
    /// converted outside the application, and <see cref="DatabaseStartupExtensions.EnsureStartsFromBaselineAsync"/> refuses one
    /// that was not.
    /// </summary>
    public partial class Baseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OAuthAuditLog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    ClientId = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: true),
                    IpAddress = table.Column<string>(type: "text", nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true),
                    EventDataJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OAuthAuditLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OAuthClients",
                columns: table => new
                {
                    ClientId = table.Column<string>(type: "text", nullable: false),
                    ClientName = table.Column<string>(type: "text", nullable: false),
                    RedirectUris = table.Column<string[]>(type: "text[]", nullable: false),
                    ClientSecretHash = table.Column<string>(type: "text", nullable: true),
                    LogoUri = table.Column<string>(type: "text", nullable: true),
                    ClientUri = table.Column<string>(type: "text", nullable: true),
                    IsPublic = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OAuthClients", x => x.ClientId);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUser = table.Column<string>(type: "text", nullable: true),
                    RequestText = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Entry = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ClarifyJson = table.Column<string>(type: "jsonb", nullable: true),
                    AnswersJson = table.Column<string>(type: "jsonb", nullable: true),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IntakeOutcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    IntakeAttempts = table.Column<int>(type: "integer", nullable: false),
                    IntakeStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IntakeFinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    QuestionsAskedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AnsweredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResearchJson = table.Column<string>(type: "text", nullable: true),
                    ResearchAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Providers",
                columns: table => new
                {
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Settings = table.Column<string>(type: "jsonb", nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Providers", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "SiteSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperatorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AddressStreet = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    AddressZip = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AddressCity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AddressCountry = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ContactEmail = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ContactPhone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ResponsiblePerson = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    VatId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    RegisterInfo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    SupervisoryAuthority = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Jurisdiction = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PrivacyAppendMarkdown = table.Column<string>(type: "text", nullable: true),
                    TermsAppendMarkdown = table.Column<string>(type: "text", nullable: true),
                    LegalBoilerplateAccepted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SourceRefusals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OwnerUsername = table.Column<string>(type: "text", nullable: false),
                    Path = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Extension = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    DeclaredType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceRefusals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUsername = table.Column<string>(type: "text", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character(64)", nullable: false),
                    StoragePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ExtractedText = table.Column<string>(type: "text", nullable: true),
                    PageCount = table.Column<int>(type: "integer", nullable: true),
                    CharCount = table.Column<int>(type: "integer", nullable: false),
                    WordCount = table.Column<int>(type: "integer", nullable: false),
                    Quality = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    WarningsJson = table.Column<string>(type: "jsonb", nullable: false),
                    Origin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    InLibrary = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    AlwaysInclude = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserProfiles",
                columns: table => new
                {
                    Username = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Role = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Organization = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Formality = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Signature = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    NoGos = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    FreeText = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProfiles", x => x.Username);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Username = table.Column<string>(type: "text", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    IsAdmin = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "OAuthAccessTokens",
                columns: table => new
                {
                    TokenHash = table.Column<string>(type: "text", nullable: false),
                    ClientId = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Scope = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OAuthAccessTokens", x => x.TokenHash);
                    table.ForeignKey(
                        name: "FK_OAuthAccessTokens_OAuthClients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "OAuthClients",
                        principalColumn: "ClientId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OAuthAuthorizationCodes",
                columns: table => new
                {
                    CodeHash = table.Column<string>(type: "text", nullable: false),
                    ClientId = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    RedirectUri = table.Column<string>(type: "text", nullable: false),
                    Scope = table.Column<string>(type: "text", nullable: false),
                    CodeChallenge = table.Column<string>(type: "text", nullable: false),
                    CodeChallengeMethod = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OAuthAuthorizationCodes", x => x.CodeHash);
                    table.ForeignKey(
                        name: "FK_OAuthAuthorizationCodes_OAuthClients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "OAuthClients",
                        principalColumn: "ClientId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OAuthRefreshTokens",
                columns: table => new
                {
                    TokenHash = table.Column<string>(type: "text", nullable: false),
                    ClientId = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Scope = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OAuthRefreshTokens", x => x.TokenHash);
                    table.ForeignKey(
                        name: "FK_OAuthRefreshTokens_OAuthClients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "OAuthClients",
                        principalColumn: "ClientId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrderIntakeShadows",
                columns: table => new
                {
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: true),
                    QuestionCount = table.Column<int>(type: "integer", nullable: true),
                    RecordJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderIntakeShadows", x => x.OrderId);
                    table.ForeignKey(
                        name: "FK_OrderIntakeShadows_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    BriefingText = table.Column<string>(type: "text", nullable: false),
                    CreatedByUser = table.Column<string>(type: "text", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinalText = table.Column<string>(type: "text", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    TokensTotal = table.Column<int>(type: "integer", nullable: false),
                    CancellationRequested = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CrewTemplateName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CrewSnapshot = table.Column<string>(type: "jsonb", nullable: true),
                    TotalCostEur = table.Column<decimal>(type: "numeric(10,6)", nullable: true),
                    LlmCostEur = table.Column<decimal>(type: "numeric(10,6)", nullable: true),
                    CachedInputTokens = table.Column<int>(type: "integer", nullable: true),
                    ReasoningTokens = table.Column<int>(type: "integer", nullable: true),
                    ParentRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    SeedDraftText = table.Column<string>(type: "text", nullable: true),
                    RevisionHint = table.Column<string>(type: "text", nullable: true),
                    WordCount = table.Column<int>(type: "integer", nullable: true),
                    Outcome = table.Column<int>(type: "integer", nullable: true),
                    DeliveredIteration = table.Column<int>(type: "integer", nullable: true),
                    ReportJson = table.Column<string>(type: "text", nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderVersion = table.Column<int>(type: "integer", nullable: true),
                    FactCheckMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Runs_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrderSources",
                columns: table => new
                {
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    PageSelection = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderSources", x => new { x.OrderId, x.SourceId });
                    table.ForeignKey(
                        name: "FK_OrderSources_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrderSources_Sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "Sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UploadTickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUsername = table.Column<string>(type: "text", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DeclaredBytes = table.Column<long>(type: "bigint", nullable: true),
                    TokenHash = table.Column<string>(type: "character(64)", nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TokenExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReceivingSince = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastRefusalCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadTickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UploadTickets_Sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "Sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Events_Runs_RunId",
                        column: x => x.RunId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Iterations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    IterationNumber = table.Column<int>(type: "integer", nullable: false),
                    ArtifactText = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExecutorInputTokens = table.Column<int>(type: "integer", nullable: true),
                    ExecutorOutputTokens = table.Column<int>(type: "integer", nullable: true),
                    ExecutorCostEur = table.Column<decimal>(type: "numeric(10,6)", nullable: true),
                    ReviewersTotalCostEur = table.Column<decimal>(type: "numeric(10,6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Iterations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Iterations_Runs_RunId",
                        column: x => x.RunId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RunSourceUsages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Target = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CharsIncluded = table.Column<int>(type: "integer", nullable: false),
                    PagesIncluded = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunSourceUsages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RunSourceUsages_Runs_RunId",
                        column: x => x.RunId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RunSourceUsages_Sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "Sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RunWarnings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Iteration = table.Column<int>(type: "integer", nullable: true),
                    Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunWarnings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RunWarnings_Runs_RunId",
                        column: x => x.RunId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Findings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IterationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Severity = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Category = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Quote = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Findings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Findings_Iterations_IterationId",
                        column: x => x.IterationId,
                        principalTable: "Iterations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IterationActorCosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IterationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorType = table.Column<int>(type: "integer", nullable: false),
                    ActorName = table.Column<string>(type: "text", nullable: false),
                    ModelName = table.Column<string>(type: "text", nullable: false),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    CachedInputTokens = table.Column<int>(type: "integer", nullable: true),
                    ReasoningTokens = table.Column<int>(type: "integer", nullable: true),
                    CostEur = table.Column<decimal>(type: "numeric(10,6)", nullable: true),
                    ProviderName = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IterationActorCosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IterationActorCosts_Iterations_IterationId",
                        column: x => x.IterationId,
                        principalTable: "Iterations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Events_RunId",
                table: "Events",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_Findings_IterationId",
                table: "Findings",
                column: "IterationId");

            migrationBuilder.CreateIndex(
                name: "IX_IterationActorCosts_IterationId",
                table: "IterationActorCosts",
                column: "IterationId");

            migrationBuilder.CreateIndex(
                name: "IX_Iterations_RunId",
                table: "Iterations",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_OAuthAccessTokens_ClientId",
                table: "OAuthAccessTokens",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_OAuthAccessTokens_ExpiresAt",
                table: "OAuthAccessTokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_OAuthAccessTokens_UserId",
                table: "OAuthAccessTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_OAuthAuditLog_CreatedAt",
                table: "OAuthAuditLog",
                column: "CreatedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_OAuthAuthorizationCodes_ClientId",
                table: "OAuthAuthorizationCodes",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_OAuthAuthorizationCodes_ExpiresAt",
                table: "OAuthAuthorizationCodes",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_OAuthRefreshTokens_ClientId",
                table: "OAuthRefreshTokens",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderIntakeShadows_Pending",
                table: "OrderIntakeShadows",
                column: "CreatedAt",
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CreatedByUser_UpdatedAt",
                table: "Orders",
                columns: new[] { "CreatedByUser", "UpdatedAt", "CreatedAt" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Intake_Analyzing",
                table: "Orders",
                column: "CreatedAt",
                filter: "\"State\" = 'Analyzing'");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Intake_NeedsInput",
                table: "Orders",
                column: "QuestionsAskedAt",
                filter: "\"State\" = 'NeedsInput'");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ResearchAt",
                table: "Orders",
                column: "ResearchAt",
                filter: "\"ResearchAt\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrderSources_SourceId",
                table: "OrderSources",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_Runs_CreatedAt",
                table: "Runs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Runs_CreatedByUser_CreatedAt",
                table: "Runs",
                columns: new[] { "CreatedByUser", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Runs_Status_CompletedAt",
                table: "Runs",
                columns: new[] { "Status", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_Runs_OrderId_OrderVersion",
                table: "Runs",
                columns: new[] { "OrderId", "OrderVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RunSourceUsages_RunId",
                table: "RunSourceUsages",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_RunSourceUsages_SourceId",
                table: "RunSourceUsages",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_RunWarnings_RunId",
                table: "RunWarnings",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceRefusals_CreatedAt",
                table: "SourceRefusals",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Sources_ExpiresAt",
                table: "Sources",
                column: "ExpiresAt",
                filter: "\"ExpiresAt\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Sources_OwnerUsername_Sha256",
                table: "Sources",
                columns: new[] { "OwnerUsername", "Sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UploadTickets_ExpiresAt",
                table: "UploadTickets",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_UploadTickets_OwnerUsername_State",
                table: "UploadTickets",
                columns: new[] { "OwnerUsername", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_UploadTickets_SourceId",
                table: "UploadTickets",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "UX_UploadTickets_TokenHash",
                table: "UploadTickets",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);

            migrationBuilder.Sql(
                """
                INSERT INTO "Providers" ("Name", "DisplayName", "Description", "Type", "Settings", "IsSystem", "IsActive", "CreatedAt", "UpdatedAt")
                VALUES
                ('azure-openai', 'Azure OpenAI', 'Microsoft Azure OpenAI Service. Requires Azure subscription with OpenAI access. Clone as custom provider and configure resource_name and deployment_name.', 0, '{"endpoint": "https://{resource_name}.openai.azure.com/openai/deployments/{deployment_name}", "api_key_env": "AZURE_OPENAI_API_KEY", "auth_header_name": "api-key", "auth_header_format": "{key}"}', true, true, now(), now()),
                ('deepseek', 'DeepSeek', 'Affordable Chinese provider with strong coding and reasoning models. OpenAI-compatible. Often 10x cheaper than equivalent OpenAI models.', 0, '{"endpoint": "https://api.deepseek.com/v1", "api_key_env": "DEEPSEEK_API_KEY", "models_endpoint": "/models", "auth_header_name": "Authorization", "auth_header_format": "Bearer {key}"}', true, true, now(), now()),
                ('google-ai-studio', 'Google AI Studio (Gemini)', 'Direct connection to Google''s Gemini API via AI Studio. OpenAI-compatible endpoint.', 0, '{"endpoint": "https://generativelanguage.googleapis.com/v1beta/openai", "api_key_env": "GEMINI_API_KEY", "models_endpoint": "/models", "auth_header_name": "Authorization", "auth_header_format": "Bearer {key}"}', true, true, now(), now()),
                ('groq', 'Groq', 'Ultra-fast inference for open-source models (Llama, Mixtral). OpenAI-compatible. Free tier available with rate limits. Good for high-throughput reviewers.', 0, '{"endpoint": "https://api.groq.com/openai/v1", "api_key_env": "GROQ_API_KEY", "models_endpoint": "/models", "auth_header_name": "Authorization", "auth_header_format": "Bearer {key}"}', true, true, now(), now()),
                ('ollama-local', 'Ollama (Local)', 'Local Ollama instance for self-hosted models. No API key required. Set OLLAMA_ENDPOINT env-var to override default.', 0, '{"endpoint": "http://host.docker.internal:11434/v1", "models_endpoint": "/models", "endpoint_env_override": "OLLAMA_ENDPOINT"}', true, true, now(), now()),
                ('openai-compatible-generic', 'OpenAI-Compatible (Generic)', 'Template for any other OpenAI-compatible endpoint. Configure endpoint and API key. Use for Together AI, Fireworks, Mistral La Plateforme, local LiteLLM, or any OpenAI-compatible service.', 0, '{"endpoint": "", "api_key_env": "", "models_endpoint": "/models", "auth_header_name": "Authorization", "auth_header_format": "Bearer {key}"}', true, true, now(), now()),
                ('openai-direct', 'OpenAI (Direct)', 'Direct connection to OpenAI''s API. Use when you have an OpenAI API key and want no OpenRouter-markup. Recommended for production workloads with predictable cost.', 0, '{"endpoint": "https://api.openai.com/v1", "api_key_env": "OPENAI_API_KEY", "models_endpoint": "/models", "auth_header_name": "Authorization", "auth_header_format": "Bearer {key}"}', true, true, now(), now()),
                ('openrouter', 'OpenRouter', 'Pay-per-token aggregator with access to hundreds of models from many providers. Standard choice when you don''t have a direct subscription.', 0, '{"endpoint": "https://openrouter.ai/api/v1", "api_key_env": "LLM_OPENROUTER_API_KEY", "default_headers": {"X-Title": "Geef.Atelier", "HTTP-Referer": "https://geef.stefan-bechtel.de"}, "models_endpoint": "/models", "auth_header_name": "Authorization", "auth_header_format": "Bearer {key}"}', true, true, now(), now()),
                ('claude-cli', 'Claude Code CLI', 'Anthropic Claude via the official Claude Code CLI. Uses your Claude Pro/Max/Team subscription instead of paying per token.', 1, '{"binary": "claude", "models": ["claude-opus-latest", "claude-sonnet-latest", "claude-haiku-latest", "claude-fable-latest", "claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5", "claude-fable-5-2"], "cli_kind": "claude", "auth_volume": "/auth/claude", "auth_command": "claude auth login", "max_concurrent": 2}', true, true, now(), now()),
                ('codex-cli', 'Codex CLI', 'OpenAI via the official Codex CLI. Uses your ChatGPT Plus/Pro/Team subscription instead of paying per API token.', 1, '{"binary": "codex", "models": ["gpt-sol-latest", "gpt-astra-latest", "gpt-terra-latest", "gpt-6.2-sol", "gpt-6.2-astra", "gpt-6.1-terra", "gpt-6.1-sol", "gpt-6-astra", "gpt-6-sol"], "cli_kind": "codex", "auth_volume": "/auth/codex", "auth_command": "codex auth login", "max_concurrent": 2}', true, true, now(), now()),
                ('gemini-cli', 'Gemini CLI', 'Google Gemini via the official Gemini CLI. Free tier offers 60 requests/minute and 1000 requests/day with a Google account login.', 1, '{"binary": "gemini", "models": ["google/gemini-2.5-pro", "google/gemini-2.5-flash", "google/gemini-3.1-pro", "google/gemini-3.1-flash"], "cli_kind": "gemini", "auth_volume": "/auth/gemini", "auth_command": "gemini", "max_concurrent": 2, "auth_env_var_alternative": "GEMINI_API_KEY"}', true, true, now(), now());
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO "SiteSettings" ("Id", "OperatorName", "AddressStreet", "AddressZip", "AddressCity", "AddressCountry", "ContactEmail", "UpdatedAt")
                VALUES ('00000000-0000-0000-0000-000000000001', '[Bitte ausfüllen]', '[Straße und Hausnummer]', '[PLZ]', '[Stadt]', 'Deutschland', 'kontakt@example.com', now());
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropTable(
                name: "Findings");

            migrationBuilder.DropTable(
                name: "IterationActorCosts");

            migrationBuilder.DropTable(
                name: "OAuthAccessTokens");

            migrationBuilder.DropTable(
                name: "OAuthAuditLog");

            migrationBuilder.DropTable(
                name: "OAuthAuthorizationCodes");

            migrationBuilder.DropTable(
                name: "OAuthRefreshTokens");

            migrationBuilder.DropTable(
                name: "OrderIntakeShadows");

            migrationBuilder.DropTable(
                name: "OrderSources");

            migrationBuilder.DropTable(
                name: "Providers");

            migrationBuilder.DropTable(
                name: "RunSourceUsages");

            migrationBuilder.DropTable(
                name: "RunWarnings");

            migrationBuilder.DropTable(
                name: "SiteSettings");

            migrationBuilder.DropTable(
                name: "SourceRefusals");

            migrationBuilder.DropTable(
                name: "UploadTickets");

            migrationBuilder.DropTable(
                name: "UserProfiles");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Iterations");

            migrationBuilder.DropTable(
                name: "OAuthClients");

            migrationBuilder.DropTable(
                name: "Sources");

            migrationBuilder.DropTable(
                name: "Runs");

            migrationBuilder.DropTable(
                name: "Orders");
        }
    }
}
