using Microsoft.Extensions.Configuration;
using System.IO;

namespace RealTestMcp.Core.Configuration;

public class AppSettings
{
    public DatabaseSettings Database { get; set; } = new();
    public RealTestSettings RealTest { get; set; } = new();

    public class DatabaseSettings
    {
        private string _path;

        public DatabaseSettings()
        {
            _path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RealTestMcp", "realtest.db");
        }

        public string Path
        {
            get => _path;
            set => _path = Environment.ExpandEnvironmentVariables(value);
        }
    }

    public class RealTestSettings
    {
        public string InstallPath { get; set; } = @"C:\RealTest";

        private string _docsPath = @"C:\RealTest\Help";
        public string DocsPath
        {
            get => _docsPath;
            set => _docsPath = Environment.ExpandEnvironmentVariables(value);
        }

        private string[] _scriptPaths = [];
        public string[] ScriptPaths
        {
            get => _scriptPaths;
            set => _scriptPaths = value
                .Select(Environment.ExpandEnvironmentVariables)
                .ToArray();
        }
    }

    /// <summary>
    /// Load from appsettings.json next to the binary, then apply env var overrides.
    /// Resolves config file relative to AppContext.BaseDirectory, not working directory.
    /// </summary>
    public static AppSettings Load()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables(prefix: "REALTEST_MCP_")
            .Build();

        var settings = new AppSettings();
        config.Bind(settings);
        return settings;
    }
}
