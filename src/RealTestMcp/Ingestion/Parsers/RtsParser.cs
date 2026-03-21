namespace RealTestMcp.Ingestion.Parsers;

public record RtsFile(string FilePath, string Content);

public static class RtsParser
{
    public static RtsFile ParseFile(string filePath)
    {
        var content = File.ReadAllText(filePath);
        return new RtsFile(Path.GetFullPath(filePath), content);
    }

    public static IEnumerable<RtsFile> ParseDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
            yield break;

        foreach (var file in Directory.EnumerateFiles(directoryPath, "*.rts", SearchOption.AllDirectories))
        {
            RtsFile? result = null;
            try { result = ParseFile(file); }
            catch (Exception ex) { Console.Error.WriteLine($"[RtsParser] Skipping {file}: {ex.Message}"); }
            if (result is not null) yield return result;
        }
    }
}
