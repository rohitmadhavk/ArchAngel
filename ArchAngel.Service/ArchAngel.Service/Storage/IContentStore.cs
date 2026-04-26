using ArchAngel.Service.Content;

namespace ArchAngel.Service.Storage;

/// <summary>
/// Interface for storing and searching content chunks
/// </summary>
public interface IContentStore
{
    /// <summary>
    /// Store chunks for a repository
    /// </summary>
    Task StoreChunksAsync(string repositoryKey, List<ContentChunk> chunks);
    
    /// <summary>
    /// Search for chunks matching a query
    /// </summary>
    Task<List<ContentChunk>> SearchAsync(string query, string? repositoryFilter = null, int maxResults = 10);
    
    /// <summary>
    /// Get all indexed repositories
    /// </summary>
    Task<List<string>> GetIndexedRepositoriesAsync();
    
    /// <summary>
    /// Remove all chunks for a repository
    /// </summary>
    Task RemoveRepositoryAsync(string repositoryKey);
    
    /// <summary>
    /// Get chunk by ID
    /// </summary>
    Task<ContentChunk?> GetChunkAsync(string chunkId);
    
    /// <summary>
    /// Get statistics about stored content
    /// </summary>
    Task<ContentStoreStats> GetStatsAsync();

    Task CleanChunksAsync (string repositoryKey);

    
    Task<List<ContentChunk>> VectorSearchAsync(float[] queryEmbedding, string? repositoryFilter = null, int maxResults = 50);
    Task<List<ContentChunk>> HybridSearchAsync(float[] queryEmbedding, string query, string? repositoryFilter = null, int maxResults = 10);
    
    string GetDatabasePath();
    long GetDatabaseSizeAsync();

    Task WipeDatabaseAsync();
    string GetWorkspaceRoot();
}

/// <summary>
/// Statistics about the content store
/// </summary>
public record ContentStoreStats(
    int TotalRepositories,
    int TotalChunks,
    long TotalContentSize,
    Dictionary<string, int> LanguageDistribution,
    DateTime LastUpdated,
    List<string> files
);