// src/RealTestMcp/Program.cs
using System.CommandLine;
using RealTestMcp.Core.Configuration;

var settings = AppSettings.Load();

// ── ingest docs ──────────────────────────────────────────────────
var ingestDocsCommand = new Command("docs", "Ingest CHM documentation into the database");
ingestDocsCommand.SetAction(_ =>
{
    Console.Error.WriteLine("ingest docs: not yet implemented");
});

// ── ingest scripts ───────────────────────────────────────────────
var ingestScriptsCommand = new Command("scripts", "Ingest .rts scripts into the database");
ingestScriptsCommand.SetAction(_ =>
{
    Console.Error.WriteLine("ingest scripts: not yet implemented");
});

// ── ingest (parent) ──────────────────────────────────────────────
var ingestCommand = new Command("ingest", "Ingest data sources into the database");
ingestCommand.Subcommands.Add(ingestDocsCommand);
ingestCommand.Subcommands.Add(ingestScriptsCommand);

// ── status ───────────────────────────────────────────────────────
var statusCommand = new Command("status", "Show database statistics");
statusCommand.SetAction(_ =>
{
    var dbPath = settings.Database.Path;
    if (!File.Exists(dbPath))
    {
        Console.WriteLine("DB not initialized — run: realtest-mcp ingest docs");
        return;
    }
    Console.WriteLine("Status: DB exists (full status in Task 8)");
});

// ── root: MCP server mode ────────────────────────────────────────
var rootCommand = new RootCommand("RealTest MCP Server — semantic search for RealScript documentation");
rootCommand.Subcommands.Add(ingestCommand);
rootCommand.Subcommands.Add(statusCommand);
rootCommand.SetAction(async (_, cancellationToken) =>
{
    Console.Error.WriteLine("MCP server mode: not yet implemented");
    await Task.CompletedTask;
});

return rootCommand.Parse(args).Invoke();
