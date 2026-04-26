using System.Text;
using ArchAngel.Contracts;
using ArchAngel.Service.Utilities;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace ArchAngel.Service.Chat;
public class SKMemoryChatService : ChatServiceBase
{

    private readonly Kernel _kernel;


    private readonly ChatHistory _chatHistory = new();


    public SKMemoryChatService(
        Kernel kernel,
        ILogger<SKMemoryChatService> logger,
        ICitationProcessor citationProcessor,
        IProjectContextBuilder projectContextBuilder,
        GoldenRepoSearchService goldenRepoSearchService)
        : base(citationProcessor, projectContextBuilder, goldenRepoSearchService, logger)
    {
        _kernel = kernel;
    }
    

    private void TrimHistory(int maxTurns)
    {
        // Keep system message + last N user/assistant pairs
        var maxMessages = 1 + (maxTurns * 2);
        while (_chatHistory.Count > maxMessages)
        {
            _chatHistory.RemoveAt(1); // Remove oldest after system message
        }
    } 

    protected override async Task<string> InvokePromptAsync(string prompt)
    {
        if(_chatHistory.Count == 0){
                _chatHistory.AddSystemMessage(PromptConfig.ChatAgentSystemPrompt);
        }
        _chatHistory.AddUserMessage(prompt.ToString());

        var chatService = _kernel.GetRequiredService<IChatCompletionService>();
        var response = await chatService.GetChatMessageContentAsync(_chatHistory);
        
        var responseText = response.Content ?? "";
        _chatHistory.AddAssistantMessage(responseText);

        TrimHistory(maxTurns: 20);
        return responseText;
    }

    protected override string BuildRAGPrompt(string message, string currentProjectFiles, string goldenContext)
    {
        var userMessage = new StringBuilder();

        if (!string.IsNullOrEmpty(currentProjectFiles))
        {
            userMessage.AppendLine("## CURRENT PROJECT:");
            userMessage.AppendLine(currentProjectFiles);
            userMessage.AppendLine();
        }

        if (!string.IsNullOrEmpty(goldenContext))
        {
            userMessage.AppendLine("## GOLDEN REPOSITORY PATTERNS:");
            userMessage.AppendLine(goldenContext);
            userMessage.AppendLine();
        }

        userMessage.AppendLine("## DEVELOPER:");
        userMessage.AppendLine(message);
        return userMessage.ToString();
    }
    
    
}