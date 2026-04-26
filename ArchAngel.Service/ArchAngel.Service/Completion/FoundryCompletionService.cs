using Azure.AI.Projects;
using ArchAngel.Service.Storage;
using Azure.AI.Inference;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace ArchAngel.Service.Completion;
class FoundryCompletionService : CompletionServiceBase
{
    private readonly AIProjectClient _aiProjectClient;

    private readonly string _completionDeploymentName;

    

    public FoundryCompletionService(
        AIProjectClient aIProjectClient,
        IContentStoreProvider contentStoreProvider,
        IOptions<CompletionServiceOptions> completionServiceOptions,
        ILogger<FoundryCompletionService> logger,
        GoldenRepoSearchService goldenRepoSearchService
    ) : base(goldenRepoSearchService, contentStoreProvider, logger)
    {
        _aiProjectClient = aIProjectClient;
        _completionDeploymentName = completionServiceOptions.Value.completionDeploymentName;
    }
    

    protected override async Task<string> InvokePromptAsync(string prompt, CancellationToken cancellationToken)
    {
        var chatClient = _aiProjectClient.GetProjectOpenAIClient().GetChatClient(_completionDeploymentName);
        var response = await chatClient.CompleteChatAsync(
            [new UserChatMessage(prompt)],
            cancellationToken: cancellationToken
        );
        return response.Value.Content[0].Text;

    }
}