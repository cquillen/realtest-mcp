// src/RealTestMcp/Program.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using RealTestMcp.Tools;
using System.CommandLine;
using RealTestMcp.Core.Configuration;
using RealTestMcp.Core;
using RealTestMcp.Ingestion.Commands;

var settings = AppSettings.Load();

// ── ingest docs ──────────────────────────────────────────────────
var ingestDocsCommand = new Command("docs", "Ingest CHM documentation into the database");
ingestDocsCommand.SetAction(async (ParseResult _) =>
    await IngestDocsCommand.RunAsync(settings));

// ── ingest scripts ───────────────────────────────────────────────
var ingestScriptsCommand = new Command("scripts", "Ingest .rts scripts into the database");
ingestScriptsCommand.SetAction(async (ParseResult _) =>
    await IngestScriptsCommand.RunAsync(settings));

// ── ingest (parent) ──────────────────────────────────────────────
var ingestCommand = new Command("ingest", "Ingest data sources into the database");
ingestCommand.Subcommands.Add(ingestDocsCommand);
ingestCommand.Subcommands.Add(ingestScriptsCommand);

// ── status ───────────────────────────────────────────────────────
var statusCommand = new Command("status", "Show database statistics");
statusCommand.SetAction(async (ParseResult _) =>
{
    var dbPath = settings.Database.Path;
    if (!File.Exists(dbPath))
    {
        Console.WriteLine("DB not initialized — run: realtest-mcp ingest docs");
        return;
    }

    await using var store = new VectorStoreService(dbPath);
    await store.EnsureSchemaAsync();

    var counts = await store.GetChunkCountsAsync();
    var dbSize = new FileInfo(dbPath).Length / 1024.0 / 1024.0;

    var docsLast    = await store.GetLastIngestTimeAsync("docs");
    var exampleLast = await store.GetLastIngestTimeAsync("example");
    var userLast    = await store.GetLastIngestTimeAsync("user_script");

    Console.WriteLine("RealTest MCP — Database Status");
    Console.WriteLine("================================");
    Console.WriteLine($"DB path:        {dbPath}");
    Console.WriteLine($"DB size:        {dbSize:F1} MB");
    Console.WriteLine();
    Console.WriteLine("Chunk counts:");
    Console.WriteLine($"  docs           {counts.GetValueOrDefault("docs"),6}");
    Console.WriteLine($"  example        {counts.GetValueOrDefault("example"),6}");
    Console.WriteLine($"  user_script    {counts.GetValueOrDefault("user_script"),6}");
    Console.WriteLine($"  {"─────────────────"}");
    Console.WriteLine($"  total          {counts.Values.Sum(),6}");
    Console.WriteLine();
    Console.WriteLine("Model:          all-MiniLM-L6-v2 (SmartComponents.LocalEmbeddings, bundled)");
    Console.WriteLine();
    Console.WriteLine("Last ingest:");
    Console.WriteLine($"  docs        {docsLast ?? "never"}");
    Console.WriteLine($"  scripts     {exampleLast ?? userLast ?? "never"}");
});

// ── root: MCP server mode ────────────────────────────────────────
var rootCommand = new RootCommand("RealTest MCP Server — semantic search for RealScript documentation");
rootCommand.Subcommands.Add(ingestCommand);
rootCommand.Subcommands.Add(statusCommand);
rootCommand.SetAction(async (_, cancellationToken) =>
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Services
        .AddSingleton(settings)
        .AddSingleton(new VectorStoreService(settings.Database.Path))
        .AddSingleton<EmbeddingService>()
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly(typeof(SearchDocsTool).Assembly);

    var host = builder.Build();
    var store = host.Services.GetRequiredService<VectorStoreService>();
    await store.EnsureSchemaAsync();
    await host.RunAsync(cancellationToken);
});

return rootCommand.Parse(args).Invoke();
