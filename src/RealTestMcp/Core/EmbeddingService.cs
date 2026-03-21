using SmartComponents.LocalEmbeddings;

namespace RealTestMcp.Core;

public class EmbeddingService : IDisposable
{
    private readonly LocalEmbedder _embedder = new();

    public Task<float[]> EmbedAsync(string text)
    {
        var embedding = _embedder.Embed(text);
        return Task.FromResult(embedding.Values.ToArray());
    }

    public async Task<float[][]> EmbedBatchAsync(IEnumerable<string> texts)
    {
        var results = new List<float[]>();
        foreach (var text in texts)
            results.Add(await EmbedAsync(text));
        return [.. results];
    }

    public void Dispose() => _embedder.Dispose();
}
