using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;

namespace ArchAngel.Service.Github;

/// <summary>
/// Concrete implementation of GitHub HTTP client
/// </summary>
public class GitHubHTTPClient : IGithubHTTPClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubHTTPClient> _logger;
    private string? _authToken;

    public GitHubHTTPClient(HttpClient httpClient, ILogger<GitHubHTTPClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        ConfigureHttpClient();
    }

    public bool IsAuthenticated => !string.IsNullOrEmpty(_authToken);

    public void SetAuthToken(string authToken)
    {
        _authToken = authToken;
        _httpClient.DefaultRequestHeaders.Authorization = !string.IsNullOrEmpty(authToken)
            ? new AuthenticationHeaderValue("Bearer", authToken)
            : null;
        _logger.LogDebug("GitHub auth token {Status}", IsAuthenticated ? "configured" : "cleared");
    }


    private void ConfigureHttpClient()
    {
        _httpClient.BaseAddress = new Uri("https://api.github.com/");
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "archAngel/1.0");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
        _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

        if (IsAuthenticated)
        {
            _httpClient.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Bearer", _authToken);
            _logger.LogDebug("GitHub client configured with authentication");
        }
        else
        {
            _logger.LogWarning("GitHub client configured without authentication (rate limited)");
        }
    }

    public async Task<GitHubRepository> GetRepositoryAsync(string owner, string name)
    {
        try
        {
            _logger.LogDebug("Getting repository info for {Owner}/{Name}", owner, name);
            
            var response = await _httpClient.GetAsync($"repos/{owner}/{name}");
            await EnsureSuccessStatusCodeAsync(response);
            
            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            return new GitHubRepository(
                Name: root.GetProperty("name").GetString() ?? "",
                FullName: root.GetProperty("full_name").GetString() ?? "",
                DefaultBranch: root.GetProperty("default_branch").GetString() ?? "main",
                IsPrivate: root.GetProperty("private").GetBoolean(),
                Size: root.GetProperty("size").GetInt32(),
                Language: root.TryGetProperty("language", out var lang) ? lang.GetString() ?? "Unknown" : "Unknown",
                UpdatedAt: root.GetProperty("updated_at").GetDateTime()
            );
        }
        catch (Exception ex) when (!(ex is GitHubApiException))
        {
            _logger.LogError(ex, "Failed to get repository {Owner}/{Name}", owner, name);
            throw new GitHubApiException(500, $"Failed to get repository: {ex.Message}");
        }
    }

    public async Task<GitHubTree> GetTreeAsync(string owner, string name, string sha)
    {
        try
        {
            _logger.LogDebug("Getting tree for {Owner}/{Name}@{Sha}", owner, name, sha);
            
            var response = await _httpClient.GetAsync($"repos/{owner}/{name}/git/trees/{sha}?recursive=1");
            await EnsureSuccessStatusCodeAsync(response);
            
            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            var treeItems = new List<GitHubTreeItem>();
            if (root.TryGetProperty("tree", out var treeArray))
            {
                foreach (var item in treeArray.EnumerateArray())
                {
                    treeItems.Add(new GitHubTreeItem(
                        Path: item.GetProperty("path").GetString() ?? "",
                        Mode: item.GetProperty("mode").GetString() ?? "",
                        Type: item.GetProperty("type").GetString() ?? "",
                        Sha: item.GetProperty("sha").GetString() ?? "",
                        Size: item.TryGetProperty("size", out var size) ? size.GetInt32() : null,
                        Url: item.GetProperty("url").GetString() ?? ""
                    ));
                }
            }
            
            return new GitHubTree(
                Sha: root.GetProperty("sha").GetString() ?? "",
                Url: root.GetProperty("url").GetString() ?? "",
                Tree: treeItems.ToArray(),
                Truncated: root.TryGetProperty("truncated", out var truncated) && truncated.GetBoolean()
            );
        }
        catch (Exception ex) when (!(ex is GitHubApiException))
        {
            _logger.LogError(ex, "Failed to get tree for {Owner}/{Name}@{Sha}", owner, name, sha);
            throw new GitHubApiException(500, $"Failed to get tree: {ex.Message}");
        }
    }

    public async Task<GitHubBlob> GetBlobAsync(string owner, string name, string sha)
    {
        try
        {
            _logger.LogDebug("Getting blob {Sha} from {Owner}/{Name}", sha, owner, name);
            
            var response = await _httpClient.GetAsync($"repos/{owner}/{name}/git/blobs/{sha}");
            await EnsureSuccessStatusCodeAsync(response);
            
            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            return new GitHubBlob(
                Sha: root.GetProperty("sha").GetString() ?? "",
                Url: root.GetProperty("url").GetString() ?? "",
                Content: root.GetProperty("content").GetString() ?? "",
                Encoding: root.GetProperty("encoding").GetString() ?? "",
                Size: root.GetProperty("size").GetInt32()
            );
        }
        catch (Exception ex) when (!(ex is GitHubApiException))
        {
            _logger.LogError(ex, "Failed to get blob {Sha} from {Owner}/{Name}", sha, owner, name);
            throw new GitHubApiException(500, $"Failed to get blob: {ex.Message}");
        }
    }

    public async Task<GitHubRateLimit> GetRateLimitAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("rate_limit");
            await EnsureSuccessStatusCodeAsync(response);
            
            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var core = doc.RootElement.GetProperty("resources").GetProperty("core");
            
            return new GitHubRateLimit(
                Limit: core.GetProperty("limit").GetInt32(),
                Remaining: core.GetProperty("remaining").GetInt32(),
                Used: core.GetProperty("used").GetInt32(),
                ResetTime: DateTimeOffset.FromUnixTimeSeconds(core.GetProperty("reset").GetInt64()).DateTime
            );
        }
        catch (Exception ex) when (!(ex is GitHubApiException))
        {
            _logger.LogError(ex, "Failed to get rate limit");
            throw new GitHubApiException(500, $"Failed to get rate limit: {ex.Message}");
        }
    }

    private async Task EnsureSuccessStatusCodeAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        
        var errorContent = await response.Content.ReadAsStringAsync();
        var statusCode = (int)response.StatusCode;
        
        string? errorMessage = null;
        try
        {
            var errorDoc = JsonDocument.Parse(errorContent);
            errorMessage = errorDoc.RootElement.GetProperty("message").GetString();
        }
        catch
        {
            errorMessage = errorContent;
        }
        
        _logger.LogError("GitHub API error: {StatusCode} - {ErrorMessage}", statusCode, errorMessage);
        throw new GitHubApiException(statusCode, errorMessage);
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}