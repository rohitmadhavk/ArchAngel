using System.Text;
using Azure.AI.Inference;
using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using ArchAngel.Service.Utilities;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;

namespace ArchAngel.Service.DocumentGen;

public class FoundryDocumentGenerationService : DocumentGenerationServiceBase
{
    private readonly AIProjectClient _aiProjectClient;
    private AgentSession? _agentSession;
    private ChatClientAgent? _agent;    
    private readonly string _documentGenDeploymentName;

    
    public FoundryDocumentGenerationService(
        AIProjectClient aIProjectClient,
        IOptions<DocumentGenerationOptions> documentGenOptions,
        ICitationProcessor citationProcessor, 
        GoldenRepoSearchService goldenRepoSearchService, 
        ILogger<DocumentGenerationServiceBase> logger
        ) : base(citationProcessor,goldenRepoSearchService,logger)
    {
        _aiProjectClient = aIProjectClient;
        _documentGenDeploymentName = documentGenOptions.Value.documentGenDeploymentName;
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

    private async Task CreateAgentAndSession()
    {
        _agent = await _aiProjectClient.CreateAIAgentAsync(name: "ArchAngelCodeStyleDocGenAgent", model: _documentGenDeploymentName, instructions: PromptConfig.AGENT_CODE_STYLE_SYSTEM_PROMPT);
        ProjectConversationsClient conversationsClient = _aiProjectClient.GetProjectOpenAIClient().GetProjectConversationsClient();
        ProjectConversation conversation = await conversationsClient.CreateProjectConversationAsync();

        // Providing the conversation Id is not strictly necessary, but by not providing it no information will show up in the Foundry Project UI as conversations.
        // Sessions that don't have a conversation Id will work based on the `PreviousResponseId`.
        _agentSession = await _agent.CreateSessionAsync(conversation.Id);
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
