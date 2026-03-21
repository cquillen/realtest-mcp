using RealTestMcp.Core;
using RealTestMcp.Core.Configuration;
using RealTestMcp.Ingestion.Chunkers;
using RealTestMcp.Ingestion.Parsers;

namespace RealTestMcp.Ingestion.Commands;

public static class IngestDocsCommand
{
    public static async Task RunAsync(AppSettings settings)
    {
        var docsPath = settings.RealTest.DocsPath;
        if (!Directory.Exists(docsPath))
        {
            Console.Error.WriteLine($"Docs path not found: {docsPath}");
            Console.Error.WriteLine("Extract your CHM file first: hh.exe -decompile <output_dir> <file.chm>");
            return;
        }

        Console.WriteLine($"Parsing HTML files from: {docsPath}");
        var pages = ChmParser.ParseDirectory(docsPath);
        Console.WriteLine($"Found {pages.Count} pages");

        var allChunks = pages.SelectMany(DocChunker.Chunk).ToList();
        Console.WriteLine($"Produced {allChunks.Count} chunks");

        await using var store = new VectorStoreService(settings.Database.Path);
        await store.EnsureSchemaAsync();
        using var embedder = new EmbeddingService();

        Console.WriteLine("Clearing existing docs chunks...");
        await store.DeleteBySourceTypeAsync("docs");

        Console.WriteLine("Embedding and storing chunks...");
        int count = 0;
        foreach (var chunk in allChunks)
        {
            try
            {
                var embedding = await embedder.EmbedAsync(chunk.Content);
                // Stamp with current time for last-ingest tracking
                var stamped = chunk with { CreatedAt = DateTime.UtcNow };
                await store.UpsertChunkAsync(stamped, embedding);
                count++;
                if (count % 50 == 0) Console.Write($"\r  {count}/{allChunks.Count}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"\n[Warning] Failed to process {chunk.SourcePath}: {ex.Message}");
            }
        }

        Console.WriteLine($"\nDone. Ingested {count} docs chunks.");
    }
}
