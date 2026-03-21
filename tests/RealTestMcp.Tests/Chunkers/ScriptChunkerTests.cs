using RealTestMcp.Ingestion.Chunkers;
using RealTestMcp.Ingestion.Parsers;

namespace RealTestMcp.Tests.Chunkers;

public class ScriptChunkerTests
{
    private static string ScriptsDir => Path.Combine(AppContext.BaseDirectory, "data", "scripts");

    [Fact]
    public void Chunk_ProducesOneChunkPerFile()
    {
        var script = RtsParser.ParseFile(Path.Combine(ScriptsDir, "mean-reversion.rts"));
        var chunks = ScriptChunker.Chunk(script, sourceType: "example");

        Assert.Single(chunks);
    }

    [Fact]
    public void Chunk_HasCorrectSourceType()
    {
        var script = RtsParser.ParseFile(Path.Combine(ScriptsDir, "mean-reversion.rts"));
        var example = ScriptChunker.Chunk(script, "example");
        var user = ScriptChunker.Chunk(script, "user_script");

        Assert.Equal("example", example[0].SourceType);
        Assert.Equal("user_script", user[0].SourceType);
    }

    [Fact]
    public void Chunk_AttachesCategory_WhenProvided()
    {
        var script = RtsParser.ParseFile(Path.Combine(ScriptsDir, "mean-reversion.rts"));
        var catalog = new Dictionary<string, (string Category, string Description)>
        {
            ["mean-reversion"] = ("Mean Reversion", "A simple RSI(2) mean reversion system")
        };

        var chunks = ScriptChunker.Chunk(script, "example", catalog);

        Assert.Equal("Mean Reversion", chunks[0].Category);
        Assert.Contains("RSI(2)", chunks[0].Description);
    }

    [Fact]
    public void ChunkIds_AreDeterministic()
    {
        var script = RtsParser.ParseFile(Path.Combine(ScriptsDir, "futures-example.rts"));
        var c1 = ScriptChunker.Chunk(script, "example");
        var c2 = ScriptChunker.Chunk(script, "example");

        Assert.Equal(c1[0].Id, c2[0].Id);
    }
}
