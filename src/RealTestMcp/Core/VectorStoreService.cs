using Microsoft.Data.Sqlite;
using RealTestMcp.Core.Models;
using System.Security.Cryptography;
using System.Text;

namespace RealTestMcp.Core;

public class VectorStoreService : IAsyncDisposable
{
    private readonly string _dbPath;
    private volatile SqliteConnection? _connection;
    private readonly SemaphoreSlim _initLock = new SemaphoreSlim(1, 1);

    public VectorStoreService(string dbPath)
    {
        _dbPath = dbPath;
    }

    public static string ComputeChunkId(string sourcePath, int chunkIndex)
    {
        var input = $"{sourcePath}:{chunkIndex}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<SqliteConnection> GetConnectionAsync()
    {
        if (_connection is not null) return _connection;
        await _initLock.WaitAsync();
        try
        {
            if (_connection is null)
            {
                var dir = Path.GetDirectoryName(_dbPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                _connection = new SqliteConnection($"Data Source={_dbPath};Pooling=False");
                await _connection.OpenAsync();

                _connection.EnableExtensions(true);
                _connection.LoadExtension(Path.Combine(AppContext.BaseDirectory, "vec0"));
            }
        }
        finally
        {
            _initLock.Release();
        }
        return _connection!;
    }

    public async Task EnsureSchemaAsync()
    {
        var conn = await GetConnectionAsync();

        // Each DDL statement must be executed separately
        await ExecuteNonQueryAsync(conn, """
            CREATE TABLE IF NOT EXISTS chunks (
                id           TEXT    PRIMARY KEY,
                source_type  TEXT    NOT NULL,
                source_path  TEXT    NOT NULL,
                chunk_type   TEXT    NOT NULL,
                section      TEXT,
                category     TEXT,
                description  TEXT,
                content      TEXT    NOT NULL,
                chunk_index  INTEGER NOT NULL,
                created_at   TEXT    NOT NULL
            )
            """);
        await ExecuteNonQueryAsync(conn, "CREATE INDEX IF NOT EXISTS idx_chunks_source_type ON chunks(source_type)");
        await ExecuteNonQueryAsync(conn, "CREATE INDEX IF NOT EXISTS idx_chunks_source_path ON chunks(source_path)");
        await ExecuteNonQueryAsync(conn, "CREATE INDEX IF NOT EXISTS idx_chunks_chunk_type  ON chunks(chunk_type)");

        // sqlite-vec virtual table for vector search
        await ExecuteNonQueryAsync(conn, """
            CREATE VIRTUAL TABLE IF NOT EXISTS chunk_embeddings USING vec0(
                chunk_id  TEXT PRIMARY KEY,
                embedding FLOAT[384]
            )
            """);
    }

    public async Task UpsertChunkAsync(Chunk chunk, float[] embedding)
    {
        var conn = await GetConnectionAsync();
        await ExecuteNonQueryAsync(conn, """
            INSERT OR REPLACE INTO chunks
                (id, source_type, source_path, chunk_type, section, category, description, content, chunk_index, created_at)
            VALUES
                (@id, @source_type, @source_path, @chunk_type, @section, @category, @description, @content, @chunk_index, @created_at)
            """,
            ("@id", chunk.Id),
            ("@source_type", chunk.SourceType),
            ("@source_path", chunk.SourcePath),
            ("@chunk_type", chunk.ChunkType),
            ("@section", (object?)chunk.Section ?? DBNull.Value),
            ("@category", (object?)chunk.Category ?? DBNull.Value),
            ("@description", (object?)chunk.Description ?? DBNull.Value),
            ("@content", chunk.Content),
            ("@chunk_index", chunk.ChunkIndex),
            ("@created_at", chunk.CreatedAt.ToString("O")));

        // Upsert embedding vector
        var vectorJson = "[" + string.Join(",", embedding) + "]";
        await ExecuteNonQueryAsync(conn,
            "INSERT OR REPLACE INTO chunk_embeddings (chunk_id, embedding) VALUES (@id, @vec)",
            ("@id", chunk.Id),
            ("@vec", vectorJson));
    }

    public async Task DeleteBySourceTypeAsync(string sourceType)
    {
        var conn = await GetConnectionAsync();
        // Delete embeddings first (no cascade on virtual table)
        await ExecuteNonQueryAsync(conn,
            "DELETE FROM chunk_embeddings WHERE chunk_id IN (SELECT id FROM chunks WHERE source_type = @st)",
            ("@st", sourceType));
        await ExecuteNonQueryAsync(conn,
            "DELETE FROM chunks WHERE source_type = @st",
            ("@st", sourceType));
    }

    public async Task DeleteBySourceTypesAsync(IEnumerable<string> sourceTypes)
    {
        foreach (var st in sourceTypes)
            await DeleteBySourceTypeAsync(st);
    }

    public async Task<List<SearchResult>> VectorSearchAsync(
        float[] queryEmbedding,
        string? sourceType,
        string? categoryFilter = null,
        string? sectionFilter = null,
        int topK = 5)
    {
        var conn = await GetConnectionAsync();
        var vectorJson = "[" + string.Join(",", queryEmbedding) + "]";

        // Over-fetch by 3x to ensure enough results survive the metadata filters.
        var fetchK = topK * 3;

        var whereClause = sourceType is not null ? "WHERE c.source_type = @source_type" : "WHERE 1=1";
        if (categoryFilter is not null) whereClause += " AND LOWER(c.category) = LOWER(@category)";
        if (sectionFilter  is not null) whereClause += " AND LOWER(c.section)  LIKE LOWER(@section)";

        var sql = $"""
            SELECT c.id, c.source_type, c.source_path, c.chunk_type, c.section,
                   c.category, c.description, c.content, knn.distance
            FROM chunks c
            JOIN (
                SELECT chunk_id, distance
                FROM chunk_embeddings
                WHERE embedding MATCH @vec AND k = @fetchk
            ) knn ON c.id = knn.chunk_id
            {whereClause}
            ORDER BY knn.distance
            LIMIT @topk
            """;

        var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@vec", vectorJson);
        cmd.Parameters.AddWithValue("@fetchk", fetchK);
        cmd.Parameters.AddWithValue("@topk", topK);
        if (sourceType    is not null) cmd.Parameters.AddWithValue("@source_type", sourceType);
        if (categoryFilter is not null) cmd.Parameters.AddWithValue("@category", categoryFilter);
        if (sectionFilter  is not null) cmd.Parameters.AddWithValue("@section", $"%{sectionFilter}%");

        return await ReadSearchResultsAsync(cmd);
    }

    public async Task<List<SearchResult>> KeywordSearchAsync(
        string keyword,
        string? chunkType = null,
        int topK = 3)
    {
        var conn = await GetConnectionAsync();
        var whereClause = chunkType is not null ? "AND chunk_type = @chunk_type" : "";
        var sql = $"""
            SELECT id, source_type, source_path, chunk_type, section, category, description, content, 0.0 AS distance
            FROM chunks
            WHERE LOWER(content) LIKE LOWER(@keyword)
              {whereClause}
            LIMIT @topk
            """;

        var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@keyword", $"%{keyword}%");
        cmd.Parameters.AddWithValue("@topk", topK);
        if (chunkType is not null) cmd.Parameters.AddWithValue("@chunk_type", chunkType);

        return await ReadSearchResultsAsync(cmd);
    }

    public async Task<Dictionary<string, int>> GetChunkCountsAsync()
    {
        var conn = await GetConnectionAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT source_type, COUNT(*) FROM chunks GROUP BY source_type";

        var counts = new Dictionary<string, int>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            counts[reader.GetString(0)] = reader.GetInt32(1);
        return counts;
    }

    public async Task<string?> GetLastIngestTimeAsync(string sourceType)
    {
        var conn = await GetConnectionAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MAX(created_at) FROM chunks WHERE source_type = @st";
        cmd.Parameters.AddWithValue("@st", sourceType);
        var result = await cmd.ExecuteScalarAsync();
        return result is DBNull or null ? null : (string)result;
    }

    // ── helpers ────────────────────────────────────────────────────

    private static async Task<List<SearchResult>> ReadSearchResultsAsync(SqliteCommand cmd)
    {
        const int MaxContentLength = 1500;
        var results = new List<SearchResult>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var content = reader.GetString(7);
            if (content.Length > MaxContentLength)
                content = content[..MaxContentLength] + " [truncated]";

            results.Add(new SearchResult(
                Id: reader.GetString(0),
                SourceType: reader.GetString(1),
                SourcePath: reader.GetString(2),
                ChunkType: reader.GetString(3),
                Section: reader.IsDBNull(4) ? null : reader.GetString(4),
                Category: reader.IsDBNull(5) ? null : reader.GetString(5),
                Description: reader.IsDBNull(6) ? null : reader.GetString(6),
                Content: content,
                Score: reader.GetDouble(8)));
        }
        return results;
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection conn,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            _connection.Close();
            await _connection.DisposeAsync();
            _connection = null;
        }
        _initLock.Dispose();
        SqliteConnection.ClearAllPools();
    }
}
