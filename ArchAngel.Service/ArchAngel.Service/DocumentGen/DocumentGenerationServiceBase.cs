using ArchAngel.Contracts;
using ArchAngel.Service.Storage;
using ArchAngel.Service.Utilities;

namespace ArchAngel.Service.DocumentGen;
public abstract class DocumentGenerationServiceBase : IDocumentGenerationService
{
    private readonly ICitationProcessor _citationProcessor;
    protected readonly ILogger<DocumentGenerationServiceBase> _logger;
    private readonly GoldenRepoSearchService _goldenRepoSearchService;

    public DocumentGenerationServiceBase(ICitationProcessor citationProcessor, 
        GoldenRepoSearchService goldenRepoSearchService, 
        ILogger<DocumentGenerationServiceBase> logger)
    {
        _citationProcessor = citationProcessor;
        _goldenRepoSearchService = goldenRepoSearchService;
        _logger = logger;
    }
    protected abstract string BuildCodeStylePrompt(string goldenContext);
    protected abstract string BuildWikiPrompt(string goldenContext);
    
    public async Task<GeneratedDocResponse> GenerateCodeStyleDocumentAsync(string docPath, string fileName)
    {
        string goldenContext;
        List<ContentSource> sources;
        (goldenContext, sources) = await _goldenRepoSearchService.SearchGoldenRepos("code examples", 8);

        var prompt = BuildCodeStylePrompt(goldenContext);
        var response = await InvokePromptAsync(prompt);
        
        var enhancedResponse = _citationProcessor.PostProcessCitations(response, sources);

        var outputPath = Path.Combine(docPath, fileName);
        await File.WriteAllTextAsync(outputPath, enhancedResponse);
        return new GeneratedDocResponse(true, outputPath);
    }
    public async Task<GeneratedDocResponse> GenerateWikiDocumentAsync(string docPath, string fileName)
    {
        string goldenContext;
        List<ContentSource> sources;
        (goldenContext, sources) = await _goldenRepoSearchService.SearchGoldenRepos("code examples", 8);

        var prompt = BuildWikiPrompt(goldenContext);
        var response = await InvokePromptAsync(prompt);
        
        var enhancedResponse = _citationProcessor.PostProcessCitations(response, sources);

        var outputPath = Path.Combine(docPath, fileName);
        await File.WriteAllTextAsync(outputPath, enhancedResponse);
        return new GeneratedDocResponse(true, outputPath);
    }

    protected abstract Task<string> InvokePromptAsync(string prompt);
}