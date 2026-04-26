using System.Text;
using System.Text.RegularExpressions;

namespace ArchAngel.Service.Content;

/// <summary>
/// Concrete implementation for processing and chunking code content
/// </summary>
public class CodeContentProcessor : IContentProcessor
{
    private readonly ILogger<CodeContentProcessor> _logger;
    
    // File extensions that should be processed
    private static readonly HashSet<string> ProcessableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".ts", ".js", ".tsx", ".jsx", ".py", ".java", ".cpp", ".c", ".h", ".hpp",
        ".go", ".rs", ".php", ".rb", ".swift", ".kt", ".scala", ".clj", ".fs", ".vb",
        ".sql", ".json", ".xml", ".yaml", ".yml", ".md", ".txt", ".sh", ".ps1", ".bat",
        ".html", ".css", ".scss", ".less", ".vue", ".svelte"
    };

    // Maximum file size to process (1MB)
    private const int MaxFileSize = 1024 * 1024;
    
    // Lines per chunk for different content types
    private const int DefaultLinesPerChunk = 30;
    private const int DocumentationLinesPerChunk = 50;
    private const int ConfigLinesPerChunk = 100;

    public CodeContentProcessor(ILogger<CodeContentProcessor> logger)
    {
        _logger = logger;
    }

    public async Task<List<ContentChunk>> ProcessFileAsync(string filePath, string content, string language)
    {
        try
        {
            _logger.LogDebug("Processing file: {FilePath} ({Language})", filePath, language);
            
            // Clean content
            content = CleanContent(content);
            
            if (string.IsNullOrWhiteSpace(content))
            {
                return new List<ContentChunk>();
            }

            // Split into chunks based on language and content type
            var chunks = language.ToLowerInvariant() switch
            {
                "csharp" or "typescript" or "javascript" or "java" or "cpp" => 
                    ChunkCodeByStructureAsync(content, filePath, language),
                "markdown" or "txt" => 
                    ChunkDocumentationContent(content, filePath, language),
                "json" or "yaml" or "yml" or "xml" => 
                    ChunkConfigurationContent(content, filePath, language),
                _ => 
                    ChunkByLines(content, filePath, language, DefaultLinesPerChunk)
            };

            _logger.LogDebug("Created {ChunkCount} chunks for {FilePath}", chunks.Count, filePath);
            return chunks;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process file: {FilePath}", filePath);
            return new List<ContentChunk>();
        }
    }

    public bool ShouldProcessFile(string filePath, int? fileSize = null)
    {
        // Check file size
        if (fileSize.HasValue && fileSize.Value > MaxFileSize)
        {
            return false;
        }

        // Check extension
        var extension = Path.GetExtension(filePath);
        if (!ProcessableExtensions.Contains(extension))
        {
            return false;
        }

        // Skip common non-source directories
        var pathLower = filePath.ToLowerInvariant();
        var skipDirectories = new[] { "node_modules", "bin", "obj", ".git", "dist", "build", "target", "__pycache__" };
        
        if (skipDirectories.Any(dir => pathLower.Contains($"/{dir}/") || pathLower.Contains($"\\{dir}\\")))
        {
            return false;
        }

        // Skip minified files
        if (pathLower.Contains(".min.") || pathLower.EndsWith(".min.js") || pathLower.EndsWith(".min.css"))
        {
            return false;
        }

        return true;
    }

    public string GetLanguageFromPath(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".cs" => "csharp",
            ".ts" or ".tsx" => "typescript",
            ".js" or ".jsx" => "javascript",
            ".py" => "python",
            ".java" => "java",
            ".cpp" or ".cc" or ".cxx" => "cpp",
            ".c" => "c",
            ".h" or ".hpp" => "cpp",
            ".go" => "go",
            ".rs" => "rust",
            ".php" => "php",
            ".rb" => "ruby",
            ".swift" => "swift",
            ".kt" => "kotlin",
            ".scala" => "scala",
            ".json" => "json",
            ".xml" => "xml",
            ".yaml" or ".yml" => "yaml",
            ".md" => "markdown",
            ".sql" => "sql",
            ".html" => "html",
            ".css" => "css",
            ".scss" => "scss",
            ".sh" => "bash",
            ".ps1" => "powershell",
            ".bat" => "batch",
            _ => "text"
        };
    }

    public string DecodeContent(string content, string encoding)
    {
        try
        {
            return encoding.ToLowerInvariant() switch
            {
                "base64" => Encoding.UTF8.GetString(Convert.FromBase64String(content.Replace("\n", ""))),
                "utf-8" or "utf8" => content,
                _ => content
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decode content with encoding: {Encoding}", encoding);
            return content; // Return as-is if decoding fails
        }
    }

    private string CleanContent(string content)
    {
        // Normalize line endings
        content = content.Replace("\r\n", "\n").Replace("\r", "\n");
        
        // Remove excessive empty lines (more than 2 consecutive)
        content = Regex.Replace(content, @"\n{3,}", "\n\n");
        
        // Trim trailing whitespace from lines
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].TrimEnd();
        }
        
        return string.Join('\n', lines).Trim();
    }

    private List<ContentChunk> ChunkCodeByStructureAsync(string content, string filePath, string language)
    {
        var chunks = new List<ContentChunk>();
        var lines = content.Split('\n');
        
        // Try to detect logical boundaries (classes, functions, etc.)
        var boundaries = DetectLogicalBoundaries(content, language);
        
        if (boundaries.Any())
        {
            // Chunk by logical boundaries
            for (int i = 0; i < boundaries.Count; i++)
            {
                var start = boundaries[i];
                var end = i + 1 < boundaries.Count ? boundaries[i + 1] - 1 : lines.Length - 1;
                
                var chunkLines = lines[start..Math.Min(end + 1, lines.Length)];
                var chunkContent = string.Join('\n', chunkLines).Trim();
                
                if (!string.IsNullOrWhiteSpace(chunkContent))
                {
                    chunks.Add(CreateChunk(chunkContent, filePath, language, i, start, end));
                }
            }
        }
        else
        {
            // Fallback to line-based chunking
            chunks.AddRange(ChunkByLines(content, filePath, language, DefaultLinesPerChunk));
        }
        
        return chunks;
    }

    private List<int> DetectLogicalBoundaries(string content, string language)
    {
        var boundaries = new List<int> { 0 }; // Always start at line 0
        var lines = content.Split('\n');
        
        var patterns = language.ToLowerInvariant() switch
        {
            "csharp" => new[]
            {
                @"^\s*(public|private|protected|internal).*class\s+\w+",
                @"^\s*(public|private|protected|internal).*interface\s+\w+",
                @"^\s*(public|private|protected|internal).*enum\s+\w+", 
                @"^\s*(public|private|protected|internal).*struct\s+\w+",
                @"^\s*(public|private|protected|internal).*\w+\s*\([^)]*\)\s*{"
            },
            "typescript" or "javascript" => new[]
            {
                @"^(export\s+)?(class|interface)\s+\w+",
                @"^(export\s+)?(async\s+)?function\s+\w+",
                @"^(export\s+)?const\s+\w+\s*=\s*(async\s*)?\([^)]*\)\s*=>",
                @"^(export\s+)?const\s+\w+\s*=\s*{",
                @"^\w+\s*:\s*(async\s*)?\([^)]*\)\s*=>"
            },
            "java" => new[]
            {
                @"^\s*(public|private|protected).*class\s+\w+",
                @"^\s*(public|private|protected).*interface\s+\w+",
                @"^\s*(public|private|protected).*enum\s+\w+",
                @"^\s*(public|private|protected).*\w+\s*\([^)]*\)\s*{"
            },
            "python" => new[]
            {
                @"^class\s+\w+",
                @"^def\s+\w+",
                @"^async\s+def\s+\w+"
            },
            _ => Array.Empty<string>()
        };

        for (int i = 1; i < lines.Length; i++)
        {
            foreach (var pattern in patterns)
            {
                if (Regex.IsMatch(lines[i], pattern, RegexOptions.IgnoreCase))
                {
                    boundaries.Add(i);
                    break;
                }
            }
        }

        return boundaries.Distinct().OrderBy(x => x).ToList();
    }

    private List<ContentChunk> ChunkDocumentationContent(string content, string filePath, string language)
    {
        // For markdown and text files, chunk by headers or paragraphs
        var chunks = new List<ContentChunk>();
        var lines = content.Split('\n');
        var currentChunk = new List<string>();
        var chunkIndex = 0;
        var startLine = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            
            // Start new chunk on headers (markdown) or after reaching line limit
            if ((line.StartsWith('#') && currentChunk.Any()) || currentChunk.Count >= DocumentationLinesPerChunk)
            {
                if (currentChunk.Any())
                {
                    var chunkContent = string.Join('\n', currentChunk).Trim();
                    if (!string.IsNullOrWhiteSpace(chunkContent))
                    {
                        chunks.Add(CreateChunk(chunkContent, filePath, language, chunkIndex++, startLine, i - 1));
                    }
                }
                
                currentChunk.Clear();
                startLine = i;
            }
            
            currentChunk.Add(line);
        }

        // Add final chunk
        if (currentChunk.Any())
        {
            var chunkContent = string.Join('\n', currentChunk).Trim();
            if (!string.IsNullOrWhiteSpace(chunkContent))
            {
                chunks.Add(CreateChunk(chunkContent, filePath, language, chunkIndex, startLine, lines.Length - 1));
            }
        }

        return chunks;
    }

    private List<ContentChunk> ChunkConfigurationContent(string content, string filePath, string language)
    {
        // For config files, use larger chunks since they're usually more structured
        return ChunkByLines(content, filePath, language, ConfigLinesPerChunk);
    }

    private List<ContentChunk> ChunkByLines(string content, string filePath, string language, int linesPerChunk)
    {
        var chunks = new List<ContentChunk>();
        var lines = content.Split('\n');
        
        for (int i = 0; i < lines.Length; i += linesPerChunk)
        {
            var chunkLines = lines.Skip(i).Take(linesPerChunk);
            var chunkContent = string.Join('\n', chunkLines).Trim();
            
            if (!string.IsNullOrWhiteSpace(chunkContent))
            {
                var chunkIndex = i / linesPerChunk;
                var startLine = i;
                var endLine = Math.Min(i + linesPerChunk - 1, lines.Length - 1);
                
                chunks.Add(CreateChunk(chunkContent, filePath, language, chunkIndex, startLine, endLine));
            }
        }
        
        return chunks;
    }

    private ContentChunk CreateChunk(string content, string filePath, string language, int chunkIndex, int startLine, int endLine)
    {
        var id = $"{filePath}:chunk_{chunkIndex}";
        var metadata = new Dictionary<string, object>
        {
            ["fileSize"] = content.Length,
            ["extension"] = Path.GetExtension(filePath),
            ["directory"] = Path.GetDirectoryName(filePath) ?? "",
            ["fileName"] = Path.GetFileName(filePath),
            ["processedAt"] = DateTime.UtcNow
        };

        return new ContentChunk(id, content, filePath, language, chunkIndex, startLine, endLine, metadata);
    }
}