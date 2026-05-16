using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20260519120000_Step19McpOAuth")]
    public partial class Step19McpOAuth : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE ""OAuthClients"" (
    ""ClientId"" text PRIMARY KEY,
    ""ClientName"" text NOT NULL,
    ""RedirectUris"" text[] NOT NULL,
    ""ClientSecretHash"" text NULL,
    ""LogoUri"" text NULL,
    ""ClientUri"" text NULL,
    ""IsPublic"" boolean NOT NULL DEFAULT true,
    ""CreatedAt"" timestamptz NOT NULL DEFAULT now(),
    ""UpdatedAt"" timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE ""OAuthAuthorizationCodes"" (
    ""CodeHash"" text PRIMARY KEY,
    ""ClientId"" text NOT NULL REFERENCES ""OAuthClients""(""ClientId"") ON DELETE CASCADE,
    ""UserId"" text NOT NULL,
    ""RedirectUri"" text NOT NULL,
    ""Scope"" text NOT NULL,
    ""CodeChallenge"" text NOT NULL,
    ""CodeChallengeMethod"" text NOT NULL,
    ""ExpiresAt"" timestamptz NOT NULL,
    ""UsedAt"" timestamptz NULL,
    ""CreatedAt"" timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ""IX_OAuthAuthorizationCodes_ExpiresAt"" ON ""OAuthAuthorizationCodes""(""ExpiresAt"");

CREATE TABLE ""OAuthAccessTokens"" (
    ""TokenHash"" text PRIMARY KEY,
    ""ClientId"" text NOT NULL REFERENCES ""OAuthClients""(""ClientId"") ON DELETE CASCADE,
    ""UserId"" text NOT NULL,
    ""Scope"" text NOT NULL,
    ""ExpiresAt"" timestamptz NOT NULL,
    ""RevokedAt"" timestamptz NULL,
    ""CreatedAt"" timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ""IX_OAuthAccessTokens_ClientId"" ON ""OAuthAccessTokens""(""ClientId"");
CREATE INDEX ""IX_OAuthAccessTokens_UserId"" ON ""OAuthAccessTokens""(""UserId"");
CREATE INDEX ""IX_OAuthAccessTokens_ExpiresAt"" ON ""OAuthAccessTokens""(""ExpiresAt"");

CREATE TABLE ""OAuthRefreshTokens"" (
    ""TokenHash"" text PRIMARY KEY,
    ""ClientId"" text NOT NULL REFERENCES ""OAuthClients""(""ClientId"") ON DELETE CASCADE,
    ""UserId"" text NOT NULL,
    ""Scope"" text NOT NULL,
    ""ExpiresAt"" timestamptz NOT NULL,
    ""UsedAt"" timestamptz NULL,
    ""RevokedAt"" timestamptz NULL,
    ""CreatedAt"" timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ""IX_OAuthRefreshTokens_ClientId"" ON ""OAuthRefreshTokens""(""ClientId"");

CREATE TABLE ""OAuthAuditLog"" (
    ""Id"" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    ""EventType"" text NOT NULL,
    ""ClientId"" text NULL,
    ""UserId"" text NULL,
    ""IpAddress"" text NULL,
    ""UserAgent"" text NULL,
    ""EventDataJson"" jsonb NULL,
    ""CreatedAt"" timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ""IX_OAuthAuditLog_CreatedAt"" ON ""OAuthAuditLog""(""CreatedAt"" DESC);
CREATE INDEX ""IX_OAuthAuditLog_EventType"" ON ""OAuthAuditLog""(""EventType"");
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP TABLE IF EXISTS ""OAuthAuditLog"";
DROP TABLE IF EXISTS ""OAuthRefreshTokens"";
DROP TABLE IF EXISTS ""OAuthAccessTokens"";
DROP TABLE IF EXISTS ""OAuthAuthorizationCodes"";
DROP TABLE IF EXISTS ""OAuthClients"";
");
        }
    }
}
