using System.Text;
using ArchAngel.Contracts;
using ArchAngel.Service.Utilities;

namespace ArchAngel.Service.Chat;
public abstract class ChatServiceBase : IChatService
{
    private readonly ICitationProcessor _citationProcessor;
    private readonly IProjectContextBuilder _projectContextBuilder;
    private readonly GoldenRepoSearchService _goldenRepoSearchService;
    private readonly ILogger<ChatServiceBase> _logger;

    protected ChatServiceBase(
        ICitationProcessor citationProcessor, 
        IProjectContextBuilder projectContextBuilder, 
        GoldenRepoSearchService goldenRepoSearchService,
        ILogger<ChatServiceBase> logger)
    {
        _citationProcessor = citationProcessor;
        _projectContextBuilder = projectContextBuilder;
        _goldenRepoSearchService = goldenRepoSearchService;
        _logger = logger;
    }

    

    public async Task<ArchAngelChatResponse> ChatAsync(string message, Dictionary<string, string> sessionDocuments, string? codeContext = null, string? filePath = null)
    {
        return await ChatWithRAGAsync(message, includeRAG: false, filePath, sessionDocuments);
    }

    public async Task<ArchAngelChatResponse> ChatWithRAGAsync(string message, bool? includeRAG, string? filePath, Dictionary<string, string> sessionDocuments)
    {
        try
        {
             _logger.LogDebug($"[DEBUG] RAG Chat request: {message}");
            
            string currentProjectFiles = _projectContextBuilder.GetProjectContext(filePath,sessionDocuments);
            _logger.LogDebug($"[DEBUG] RAG Chat request: {currentProjectFiles}");
            var goldenContext = "";
            var sources = new List<ContentSource>();

            if (includeRAG ?? true)
            {
                (goldenContext, sources) = await _goldenRepoSearchService.SearchGoldenRepos(currentProjectFiles, 8);
            }
            
            var prompt = BuildRAGPrompt(message, currentProjectFiles, goldenContext);
            var responseText = await InvokePromptAsync(prompt);
            var enhancedResponse = _citationProcessor.PostProcessCitations(responseText, sources);

            return new ArchAngelChatResponse(
                enhancedResponse,
                Guid.NewGuid().ToString(),
                null,
                sources,
                goldenContext
            );
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[ERROR] RAG Chat failed: {ex.Message}");
            return new ArchAngelChatResponse($"Error: {ex.Message}", Guid.NewGuid().ToString());
        }
    } 

    protected abstract string BuildRAGPrompt(string message, string currentProjectFiles, string goldenContext);
    protected abstract Task<string> InvokePromptAsync(string prompt);
}