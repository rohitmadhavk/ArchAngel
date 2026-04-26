using System.ComponentModel;
using ArchAngel.Contracts;
using ArchAngel.Service.Content;
using ArchAngel.Service.Storage;
using ArchAngel.Service.Utilities;
using ModelContextProtocol.Server;

public class GoldenRepoSearchService
{
    private readonly IContentStoreProvider _contentStoreProvider;
    private readonly ILogger<GoldenRepoSearchService> _logger;
    private readonly ICitationProcessor _citationProcessor;
    public GoldenRepoSearchService(IContentStoreProvider contentStoreProvider,
        ILogger<GoldenRepoSearchService> logger,
        ICitationProcessor citationProcessor)
    {
        _contentStoreProvider = contentStoreProvider;
        _logger = logger;
        _citationProcessor = citationProcessor;
    }
    
    public async Task<(string goldenContext, List<ContentSource> sources)> SearchGoldenRepos(string searchQuery, int maxResults)
    {
        // Use hybrid search for better results
        IContentStore contentStore = _contentStoreProvider.GetContentStore();
        List<ContentChunk> searchResults = await contentStore.SearchAsync(searchQuery, null, maxResults);
        
        _logger.LogDebug($"[DEBUG] Search found {searchResults.Count} results");
        
        // Create numbered sources for citations
        var numberedSources = searchResults.Select((r, index) => new {
            Source = r,
            CitationId = index + 1
        }).ToList();
        
        // Build context with citation markers
        var goldenContext = string.Join("\n\n", numberedSources.Select(ns =>
    $"## [CITE:{ns.CitationId}] {ns.Source.FilePath} ({ns.Source.Metadata.GetValueOrDefault("repository", "unknown")}) L{ns.Source.StartLine}-L{ns.Source.EndLine} | {_citationProcessor.GenerateGitHubUrl(ns.Source)}\n```{ns.Source.Language}\n{ns.Source.Content}\n```"));
        
        var sources = numberedSources.Select(ns => new ContentSource(
            ns.Source.Id, 
            ns.Source.FilePath, 
            ns.Source.Metadata.GetValueOrDefault("repository", "unknown").ToString() ?? "unknown", 
            ns.Source.Language, 
            ns.Source.ChunkIndex, 
            ns.Source.StartLine, 
            ns.Source.EndLine, 
            ns.CitationId, // Include citation ID
            null, // relevanceScore
            _citationProcessor.GenerateGitHubUrl(ns.Source)
        )).ToList();
        return (goldenContext,sources);
    } 
}