using RealTestMcp.Core;
using RealTestMcp.Core.Configuration;
using RealTestMcp.Ingestion.Chunkers;
using RealTestMcp.Ingestion.Parsers;

namespace RealTestMcp.Ingestion.Commands;

public static class IngestScriptsCommand
{
    public static async Task RunAsync(AppSettings settings)
    {
        var scriptPaths = settings.RealTest.ScriptPaths;

        await using var store = new VectorStoreService(settings.Database.Path);
        await store.EnsureSchemaAsync();
        using var embedder = new EmbeddingService();

        Console.WriteLine("Clearing all existing script chunks...");
        await store.DeleteBySourceTypesAsync(["example", "user_script"]);

        // Convention: the FIRST entry in ScriptPaths is the official RealTest examples directory
        // (source_type=example). All subsequent entries are user scripts (source_type=user_script).
        int total = 0;
        for (int i = 0; i < scriptPaths.Length; i++)
        {
            var path = scriptPaths[i];
            var sourceType = i == 0 ? "example" : "user_script";

            if (!Directory.Exists(path))
            {
                Console.Error.WriteLine($"[Warning] Script path not found, skipping: {path}");
                continue;
            }

            Console.WriteLine($"Ingesting {sourceType} scripts from: {path}");
            var scripts = RtsParser.ParseDirectory(path).ToList();
            Console.WriteLine($"  Found {scripts.Count} .rts files");

            foreach (var script in scripts)
            {
                try
                {
                    var chunks = ScriptChunker.Chunk(script, sourceType);
                    foreach (var chunk in chunks)
                    {
                        var embedding = await embedder.EmbedAsync(chunk.Content);
                        var stamped = chunk with { CreatedAt = DateTime.UtcNow };
                        await store.UpsertChunkAsync(stamped, embedding);
                        total++;
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[Warning] Failed to process {script.FilePath}: {ex.Message}");
                }
            }
        }

        Console.WriteLine($"Done. Ingested {total} script chunks.");
    }
}
