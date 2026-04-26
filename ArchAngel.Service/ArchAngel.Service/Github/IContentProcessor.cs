namespace ArchAngel.Service.Content;

/// <summary>
/// Interface for processing and chunking code content
/// </summary>
public interface IContentProcessor
{
    /// <summary>
    /// Process a file and split it into searchable chunks
    /// </summary>
    Task<List<ContentChunk>> ProcessFileAsync(string filePath, string content, string language);
    
    /// <summary>
    /// Check if a file should be processed (based on extension, size, etc.)
    /// </summary>
    bool ShouldProcessFile(string filePath, int? fileSize = null);
    
    /// <summary>
    /// Get the programming language from file path
    /// </summary>
    string GetLanguageFromPath(string filePath);
    
    /// <summary>
    /// Decode content based on encoding type
    /// </summary>
    string DecodeContent(string content, string encoding);
}

/// <summary>
/// A chunk of processed content
/// </summary>
public record ContentChunk(
    string Id,
    string Content,
    string FilePath,
    string Language,
    int ChunkIndex,
    int StartLine,
    int EndLine,
    Dictionary<string, object> Metadata,
    float[]? Embedding = null
);