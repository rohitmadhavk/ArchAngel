using ArchAngel.Service.Storage;
using ArchAngel.Service.Utilities;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;

namespace ArchAngel.Service.Completion;
class SKCompletionService : CompletionServiceBase
{
    private readonly Kernel _kernel;
    public SKCompletionService(
        Kernel kernel,
        IContentStoreProvider contentStoreProvider,
        ILogger<SKCompletionService> logger,
        GoldenRepoSearchService goldenRepoSearchService
    ) : base(goldenRepoSearchService, contentStoreProvider, logger)
    {
        _kernel = kernel;
    }
    
    protected override async Task<string> InvokePromptAsync(string prompt, CancellationToken cancellationToken)
    {
        var settings = new AzureOpenAIPromptExecutionSettings
        {
            Temperature = 0.2,
        };

        var res = await _kernel.InvokePromptAsync(
            prompt,
            new KernelArguments(settings),
            cancellationToken: cancellationToken);
        return res.ToString();
    }    
}