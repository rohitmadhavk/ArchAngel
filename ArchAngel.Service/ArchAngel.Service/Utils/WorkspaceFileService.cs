public class WorkspaceFileService : IWorkspaceFileService
{
    private readonly ILogger<WorkspaceFileService> _logger;
    public WorkspaceFileService(ILogger<WorkspaceFileService> logger)
    {
        _logger = logger;
    }
    private List<string> GetWorkspaceFiles(string workspaceRoot)
    {
        var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".java", ".cpp", ".c", ".h", ".hpp",
            ".go", ".rs", ".php", ".rb", ".swift", ".kt", ".scala", ".clj", ".fs", ".vb",
            ".sql", ".json", ".xml", ".yaml", ".yml", ".md", ".txt", ".config", ".env",
            ".toml", ".ini", ".properties", ".gitignore", ".dockerignore"
        };

        var excludedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules", "bin", "obj", ".git", ".vs", ".vscode", "dist", "build", 
            "target", "coverage", ".nyc_output", "logs", "tmp", "temp", ".cache",
            "vendor", "packages", "__pycache__", ".pytest_cache"
        };

        var files = new List<string>();
        
        try
        {
            foreach (var file in Directory.EnumerateFiles(workspaceRoot, "*", SearchOption.AllDirectories))
            {
                var extension = Path.GetExtension(file);
                var directoryPath = Path.GetDirectoryName(file) ?? "";
                var relativePath = Path.GetRelativePath(workspaceRoot, directoryPath);
                
                // Skip excluded folders
                if (excludedFolders.Any(folder => 
                    relativePath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
                            .Any(part => string.Equals(part, folder, StringComparison.OrdinalIgnoreCase))))
                    continue;
                    
                // Include allowed extensions or special files
                if (allowedExtensions.Contains(extension) || IsSpecialFile(Path.GetFileName(file)))
                {
                    files.Add(file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"[ERROR] Failed to enumerate workspace files: {ex.Message}");
        }
        
        return files.OrderBy(f => f).ToList();
    }

    private bool IsKeyFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath).ToLower();
        var keyFileNames = new[] { 
            "package.json", "package-lock.json", "yarn.lock", "tsconfig.json", 
            "vite.config", "webpack.config", "rollup.config", "next.config",
            "app.tsx", "app.jsx", "app.ts", "app.js",
            "index.html", "index.ts", "index.js", "main.ts", "main.js",
            "program.cs", "startup.cs", "appsettings.json", "web.config",
            "dockerfile", "docker-compose", ".gitignore", "readme.md",
            "requirements.txt", "pyproject.toml", "cargo.toml", "go.mod",
            "pom.xml", "build.gradle", "makefile"
        };
        
        return keyFileNames.Any(key => fileName.Contains(key));
    }

    private bool IsSourceFile(string filePath)
    {
        var sourceExtensions = new[] { 
            ".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".java", ".cpp", ".c", ".h", 
            ".go", ".rs", ".php", ".rb", ".swift", ".kt", ".scala"
        };
        return sourceExtensions.Contains(Path.GetExtension(filePath).ToLower());
    }

    private bool IsSpecialFile(string fileName)
    {
        var specialFiles = new[] {
            "dockerfile", "makefile", "rakefile", "gulpfile", "gruntfile",
            ".gitignore", ".dockerignore", ".eslintrc", ".prettierrc"
        };
        return specialFiles.Any(special => 
            fileName.Equals(special, StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith(special, StringComparison.OrdinalIgnoreCase));
    }

    

    
    
    public async Task<Dictionary<string,string>> AutoLoadWorkspaceFilesAsync(string _workspaceRoot)
    {
        var loaded = new Dictionary<string,string>();

        try
        {

            if (string.IsNullOrEmpty(_workspaceRoot)) return loaded;
            
            var workspaceFiles = GetWorkspaceFiles(_workspaceRoot);
            
            // Prioritize key files and small source files
            var filesToLoad = workspaceFiles
                .Where(f => 
                {
                    var fileInfo = new FileInfo(f);
                    return (IsKeyFile(f) && fileInfo.Length < 50_000) || // Key files under 50KB
                        (IsSourceFile(f) && fileInfo.Length < 20_000); // Source files under 20KB
                })
                .Take(25) // Limit total files to avoid memory issues
                .ToList();

            var loadedCount = 0;
            foreach (var file in filesToLoad)
            {
                try
                {
                    var content = await File.ReadAllTextAsync(file);
                    var uri = new Uri(file).ToString();
                    loaded[uri] = content;
                    loadedCount++;
                    
                    var relativePath = Path.GetRelativePath(_workspaceRoot, file);
                    _logger.LogDebug($"[DEBUG] Auto-loaded: {relativePath} ({content.Length} chars)");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[DEBUG] Failed to auto-load {file}: {ex.Message}");
                }
            }
            
            _logger.LogDebug($"[INFO] 📁 Auto-loaded {loadedCount} workspace files from {workspaceFiles.Count} total files");
        }
        catch (Exception ex)
        {
            _logger.LogError($"[ERROR] Auto-load workspace files failed: {ex.Message}");
        }
        return loaded;
    }
}