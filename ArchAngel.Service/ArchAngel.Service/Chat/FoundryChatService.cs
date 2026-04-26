using System.Text;
using Microsoft.Agents.AI;
using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Microsoft.Extensions.Options;
using ArchAngel.Service.Utilities;

namespace ArchAngel.Service.Chat;
#pragma warning disable SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

public class FoundryChatService : ChatServiceBase
{
    
    private readonly AIProjectClient _aiProjectClient;
    private AgentSession? _agentSession;
    private ChatClientAgent? _agent;    
    private readonly string _chatDeploymentName;

    public FoundryChatService(
        AIProjectClient aIProjectClient,
        IOptions<ChatServiceOptions> chatServiceOptions,
        ICitationProcessor citationProcessor,
        IProjectContextBuilder projectContextBuilder,
        ILogger<FoundryChatService> logger,
        GoldenRepoSearchService goldenRepoSearchService
        ) : base(citationProcessor, projectContextBuilder, goldenRepoSearchService, logger)
    {
        _chatDeploymentName = chatServiceOptions.Value.ChatDeploymentName;
        _aiProjectClient = aIProjectClient;
    }

    private async Task CreateAgentAndSession()
    {
        _agent = await _aiProjectClient.CreateAIAgentAsync(name: "ArchAngelChatAgent", model: _chatDeploymentName,instructions: PromptConfig.ChatAgentSystemPrompt);
        ProjectConversationsClient conversationsClient = _aiProjectClient.GetProjectOpenAIClient().GetProjectConversationsClient();
        ProjectConversation conversation = await conversationsClient.CreateProjectConversationAsync();

        _agentSession = await _agent.CreateSessionAsync(conversation.Id);
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

    protected override async Task<string> InvokePromptAsync(string prompt)
    {
        if(_agentSession == null || _agent == null){
            await CreateAgentAndSession();
        }
        if (_agent is null)
        {
            return "FoundryChatService Failed to create Agent";
        }
        var response = await _agent.RunAsync(prompt);
        return response.Text ?? "";

    }
}
#pragma warning restore SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
