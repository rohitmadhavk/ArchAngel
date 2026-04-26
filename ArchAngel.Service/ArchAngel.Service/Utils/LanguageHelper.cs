namespace ArchAngel.Service.Utilities;

public static class LanguageHelper
{
    public static string GetLanguageFromUri(string uri) =>
        Path.GetExtension(uri).ToLowerInvariant() switch
        {
            ".ts" => "TypeScript",
            ".tsx" => "TypeScript",
            ".js" => "JavaScript",
            ".jsx" => "JavaScript",
            ".cs" => "C#",
            ".py" => "Python",
            ".java" => "Java",
            ".cpp" or ".cc" or ".cxx" => "C++",
            ".c" => "C",
            ".go" => "Go",
            ".rs" => "Rust",
            _ => "Unknown"
        };
    public static string GetCommentPrefix(string language) =>
    language switch
    {
        "Python" => "# ",
        "C" or "C++" or "C#" or "TypeScript" or "JavaScript" or "Java" or "Go" or "Rust" => "// ",
        _ => "// "
    };
}