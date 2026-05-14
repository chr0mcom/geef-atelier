using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20260518120000_Step18DomainTemplates")]
    public partial class Step18DomainTemplates : Migration
    {
        // Domain-template system profiles (juristisch, akademisch, marketing) and their
        // associated reviewer/advisor profiles are defined as code constants in SystemCrew.cs
        // and served by repositories at runtime — no schema changes or DB seeding required.
        // This migration is a deployment marker for the domain-templates feature.

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No schema changes. System profiles live in SystemCrew.cs and are merged
            // with DB entries by the repository layer (ReviewerProfileRepository,
            // AdvisorProfileRepository, CrewTemplateRepository).
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No schema changes to revert.
        }
    }
}
