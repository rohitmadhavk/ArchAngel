using ArchAngel.Service.Content;

namespace ArchAngel.Service.Services;

/// <summary>
/// Interface for repository indexing operations
/// </summary>
public interface IRepositoryIndexer
{
    /// <summary>
    /// Index a GitHub repository for RAG search
    /// </summary>
    Task<IndexingResult> IndexRepositoryAsync(string owner, string name, string? branch = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get indexing progress for a repository
    /// </summary>
    Task<IndexingProgress?> GetIndexingProgressAsync(string repositoryKey);
    
    /// <summary>
    /// Cancel an ongoing indexing operation
    /// </summary>
    Task CancelIndexingAsync(string repositoryKey);
}

/// <summary>
/// Result of repository indexing operation
/// </summary>
public record IndexingResult(
    bool Success,
    string Message,
    int ProcessedFiles,
    int TotalChunks,
    TimeSpan Duration,
    string? ErrorDetails = null
);

/// <summary>
/// Progress information for ongoing indexing
/// </summary>
public record IndexingProgress(
    string RepositoryKey,
    int TotalFiles,
    int ProcessedFiles,
    int CurrentChunks,
    string CurrentFile,
    DateTime StartTime,
    bool IsCompleted
);