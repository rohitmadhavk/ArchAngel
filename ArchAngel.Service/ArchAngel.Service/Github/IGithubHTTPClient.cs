using System.Text.Json;

namespace ArchAngel.Service.Github;

/// <summary>
/// Interface for GitHub API HTTP operations
/// </summary>
public interface IGithubHTTPClient
{
    /// <summary>
    /// Get repository information
    /// </summary>
    Task<GitHubRepository> GetRepositoryAsync(string owner, string name);
    
    /// <summary>
    /// Get repository file tree (recursive)
    /// </summary>
    Task<GitHubTree> GetTreeAsync(string owner, string name, string sha);
    
    /// <summary>
    /// Get file content by SHA
    /// </summary>
    Task<GitHubBlob> GetBlobAsync(string owner, string name, string sha);
    
    /// <summary>
    /// Check if the client is authenticated
    /// </summary>
    bool IsAuthenticated { get; }
    
    /// <summary>
    /// Get current rate limit status
    /// </summary>
    Task<GitHubRateLimit> GetRateLimitAsync();

    void SetAuthToken(string authToken);
}

/// <summary>
/// GitHub repository information
/// </summary>
public record GitHubRepository(
    string Name,
    string FullName, 
    string DefaultBranch,
    bool IsPrivate,
    int Size,
    string Language,
    DateTime UpdatedAt
);

/// <summary>
/// GitHub repository tree
/// </summary>
public record GitHubTree(
    string Sha,
    string Url,
    GitHubTreeItem[] Tree,
    bool Truncated
);

/// <summary>
/// GitHub tree item (file or directory)
/// </summary>
public record GitHubTreeItem(
    string Path,
    string Mode,
    string Type, // "blob", "tree"
    string Sha,
    int? Size,
    string Url
);

/// <summary>
/// GitHub blob (file content)
/// </summary>
public record GitHubBlob(
    string Sha,
    string Url,
    string Content,
    string Encoding, // "base64", "utf-8"
    int Size
);

/// <summary>
/// GitHub API rate limit information
/// </summary>
public record GitHubRateLimit(
    int Limit,
    int Remaining,
    int Used,
    DateTime ResetTime
);

/// <summary>
/// GitHub API exception
/// </summary>
public class GitHubApiException : Exception
{
    public int StatusCode { get; }
    public string? ErrorMessage { get; }
    
    public GitHubApiException(int statusCode, string? errorMessage = null) 
        : base($"GitHub API error: {statusCode} - {errorMessage}")
    {
        StatusCode = statusCode;
        ErrorMessage = errorMessage;
    }
}