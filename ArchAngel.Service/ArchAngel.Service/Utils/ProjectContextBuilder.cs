using System.Text;
using ArchAngel.Service.Utilities;

class ProjectContextBuilder : IProjectContextBuilder
{
    private readonly ILogger<ProjectContextBuilder> _logger;
    private readonly ITextCleaner _textCleaner;

    public ProjectContextBuilder(ILogger<ProjectContextBuilder> logger,
     ITextCleaner textCleaner)
    {
        _logger = logger;
        _textCleaner = textCleaner;
    }
    public string GetProjectContext(string? filePath, Dictionary<string, string> sessionDocuments)
    {
        string currentProjectFiles;
        if (!string.IsNullOrEmpty(filePath))
        {

            _logger.LogDebug($"[DEBUG] Received filePath: {filePath}");
            var windowsPath = new Uri(filePath).LocalPath;
            // Remove leading slash on Windows (e.g., /c:/Users -> c:/Users)
            _logger.LogDebug($"[DEBUG] Original path: {windowsPath}");
            if (windowsPath.StartsWith("/") && windowsPath.Length > 3 && windowsPath[2] == ':')
            {
                windowsPath = windowsPath.Substring(1);
            }
            _logger.LogDebug($"[DEBUG] Converted to: {windowsPath}");
            var content = File.ReadAllText(windowsPath);
            currentProjectFiles = _textCleaner.CleanTextForTokenizer(content);
        }
        else
        {
            // Fallback to all open files
            currentProjectFiles = GetOpenFilesContext(sessionDocuments);
            _logger.LogDebug($"[DEBUG] No active file provided, using all open files");
        }
        return currentProjectFiles;
    }
    private string GetOpenFilesContext(Dictionary<string, string> _sessionDocuments)
    {
        var context = new StringBuilder();
        context.AppendLine("### 🗂️ Currently Open Files:");
        
        foreach (var doc in _sessionDocuments.Take(5))
        {
            var uri = doc.Key;
            var fileName = Path.GetFileName(Uri.TryCreate(uri, UriKind.Absolute, out var uriObj) ? uriObj.LocalPath : uri);
            var language = LanguageHelper.GetLanguageFromUri(uri).ToLower();
            
            // Clean the document content
            var cleanContent = _textCleaner.CleanTextForTokenizer(doc.Value);
            
            context.AppendLine($"#### {fileName}");
            context.AppendLine($"```{language}");
            context.AppendLine(cleanContent);
            context.AppendLine("```");
            context.AppendLine();
        }
        
        return context.ToString();
    }
    
    
}