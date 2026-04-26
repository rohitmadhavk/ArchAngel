using ArchAngel.Service.Utilities;
using Microsoft.SemanticKernel;

namespace ArchAngel.Service.DocumentGen;

public class SKDocumentGenerationService : DocumentGenerationServiceBase
{
    private readonly Kernel _kernel;
    public SKDocumentGenerationService(
        Kernel kernel,
        ICitationProcessor citationProcessor, 
        GoldenRepoSearchService goldenRepoSearchService, 
        ILogger<DocumentGenerationServiceBase> logger
        ) : base(citationProcessor,goldenRepoSearchService,logger)
    {
        _kernel = kernel;
    }

    protected override string BuildCodeStylePrompt(string goldenContext)
    {
        var prompt = string.Format(PromptConfig._CODE_STYLE_SYSTEM_PROMPT, goldenContext);
        return prompt;
    }
    protected override string BuildWikiPrompt(string goldenContext)
    {
        var prompt = string.Format(PromptConfig._DEEP_WIKI_SYSTEM_PROMPT, goldenContext);
        return prompt;
    }

    protected override async Task<string> InvokePromptAsync(string prompt)
    {
        var response = await _kernel.InvokePromptAsync(prompt);
        return response.ToString();
    }
}