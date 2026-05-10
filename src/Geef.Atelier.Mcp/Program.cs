// TODO: Schritt 9 — MCP-Server mit IRunService-Integration.
// Keine produktiven Implementierungen in Schritt 1. Siehe docs/04-mcp-integration.md.

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "Geef.Atelier MCP — Stub (Schritt 9)");

app.Run();
