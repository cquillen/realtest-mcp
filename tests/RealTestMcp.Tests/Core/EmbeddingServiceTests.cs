using RealTestMcp.Core;

namespace RealTestMcp.Tests.Core;

public class EmbeddingServiceTests
{
    private readonly EmbeddingService _service = new();

    [Fact]
    public async Task EmbedAsync_Returns384DimensionVector()
    {
        var embedding = await _service.EmbedAsync("ATR function for average true range");
        Assert.Equal(384, embedding.Length);
    }

    [Fact]
    public async Task EmbedAsync_SimilarTexts_HaveHigherCosineSimilarity()
    {
        var e1 = await _service.EmbedAsync("entry setup for mean reversion strategy");
        var e2 = await _service.EmbedAsync("setup conditions for a mean reversion trade");
        var e3 = await _service.EmbedAsync("futures contract expiry date calculation");

        var sim12 = CosineSimilarity(e1, e2);
        var sim13 = CosineSimilarity(e1, e3);

        Assert.True(sim12 > sim13, $"Expected similar texts ({sim12:F3}) > dissimilar ({sim13:F3})");
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        double dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
