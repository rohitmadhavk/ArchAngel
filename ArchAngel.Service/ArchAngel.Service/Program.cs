using Microsoft.SemanticKernel;
using Azure.Identity;
using ArchAngel.Service.Services;
using ArchAngel.Service.Github;
using ArchAngel.Service.Content;
using ArchAngel.Service.Storage;
using ArchAngel.Service.Chat;
using Microsoft.Extensions.AI;
using Azure.AI.OpenAI;
using Azure.AI.Projects;
using ArchAngel.Service.Utilities;
using ArchAngel.Service.Completion;
using ArchAngel.Service.DocumentGen;


// Configure Azure OpenAI with token-based authentication (most secure)
var azureFoundryEndpoint = Environment.GetEnvironmentVariable("AZURE_FOUNDRY_ENDPOINT") ?? "";
var azureOpenAiEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? "";
var chatDeploymentName = Environment.GetEnvironmentVariable("AI_MODEL_DEPLOYMENT_CHAT") ?? "gpt-4o";
var codeCompletionDeploymentName = Environment.GetEnvironmentVariable("AI_MODEL_DEPLOYMENT_COMPLETIONS") ?? "gpt-4o-mini";
var documentGenDeploymentName = Environment.GetEnvironmentVariable("AI_MODEL_DEPLOYMENT_DOCUMENTGEN") ?? "gpt-4o";

var embeddingDeploymentName = Environment.GetEnvironmentVariable("AI_MODEL_DEPLOYMENT_EMBEDDINGS") ?? "text-embedding-3-large";

if (string.IsNullOrEmpty(azureFoundryEndpoint)){
    Console.Error.WriteLine("[ERROR] AZURE_FOUNDRY_ENDPOINT");
    return;
}
if (string.IsNullOrEmpty(azureOpenAiEndpoint)){
    Console.Error.WriteLine("[ERROR] AZURE_OPENAI_ENDPOINT");
    return;
}
Console.Error.WriteLine("[DEBUG] ✅ All required environment variables are set:");
Console.Error.WriteLine($"[DEBUG]   AZURE_FOUNDRY_ENDPOINT: {azureFoundryEndpoint}");
Console.Error.WriteLine($"[DEBUG]   AZURE_OPENAI_ENDPOINT: {azureOpenAiEndpoint}");
Console.Error.WriteLine($"[DEBUG]   Chat: AI_MODEL_DEPLOYMENT_CHAT: {chatDeploymentName}");
Console.Error.WriteLine($"[DEBUG]   Completions: AI_MODEL_DEPLOYMENT_COMPLETIONS: {codeCompletionDeploymentName}");
Console.Error.WriteLine($"[DEBUG]   Document Generation: AI_MODEL_DEPLOYMENT_DOCUMENTGEN: {documentGenDeploymentName}");
Console.Error.WriteLine($"[DEBUG]   Embeddings: AI_MODEL_DEPLOYMENT_EMBEDDINGS: {embeddingDeploymentName}");

// Configure services without hosting framework to avoid any startup messages
var host = Host.CreateDefaultBuilder()
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.SetMinimumLevel(LogLevel.Debug);
        logging.AddConsole(options => {
            options.LogToStandardErrorThreshold = LogLevel.Debug;
        });
    })
    .ConfigureServices(services =>
    {
        try
        {       
            DefaultAzureCredential authCredential = new DefaultAzureCredential();

            Console.Error.WriteLine("[DEBUG] DefaultAzureCredential created - will try credential chain");

            
            #pragma warning disable CS0618
            services.AddKernel().AddAzureOpenAIChatCompletion(
                deploymentName: chatDeploymentName,
                endpoint: azureOpenAiEndpoint,
                credentials: authCredential
            ).AddAzureOpenAITextEmbeddingGeneration(
                deploymentName: embeddingDeploymentName,
                endpoint: azureOpenAiEndpoint,
                credential: authCredential
            );


            services.AddSingleton((sp) => {
                AIProjectClient aiProjectClient = new(new Uri(azureFoundryEndpoint), new DefaultAzureCredential());
                return aiProjectClient;
                });
            services.Configure<ChatServiceOptions>(options =>
            {
                options.ChatDeploymentName = chatDeploymentName;
            });
            services.Configure<CompletionServiceOptions>(options =>
            {
                options.completionDeploymentName = codeCompletionDeploymentName;
            });

            services.Configure<DocumentGenerationOptions>(options =>
            {
                options.documentGenDeploymentName = documentGenDeploymentName;
            });

            
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
            {
                var client = new AzureOpenAIClient(new Uri(azureOpenAiEndpoint), authCredential);
                return client.GetEmbeddingClient(embeddingDeploymentName)
                    .AsIEmbeddingGenerator();
            });
            services.AddSingleton<ITextCleaner,TextCleaner>();


            
            
            #pragma warning restore CS0618
            Console.Error.WriteLine("✅ Azure OpenAI configuration successful");
            
            // _logger.LogDebug("[DEBUG] Azure OpenAI chat completion service configured with Azure Identity (token-based)");
            // Pre-warm Azure OpenAI credential on background thread
            
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DEBUG] Failed to configure Azure OpenAI: {ex.Message}");
            services.AddKernel(); // Fallback to basic kernel
        }
        services.AddSingleton<GoldenRepoSearchService>();
        services.AddSingleton<ICitationProcessor, CitationProcessor>();
        services.AddSingleton<IProjectContextBuilder, ProjectContextBuilder>();
        services.AddHttpClient<GitHubHTTPClient>();
        services.AddSingleton<IGithubHTTPClient, GitHubHTTPClient>();
        services.AddSingleton<IContentProcessor, CodeContentProcessor>();
        // Content store and repository indexer will be created when workspace is known
        services.AddSingleton<ArchAngel.Service.Configuration.ConfigurationManager>();
        

        services.AddSingleton<Func<IContentStore,IRepositoryIndexer>>(sp =>
            {
                return contentStore => new RepositoryIndexer(
                    sp.GetRequiredService<IGithubHTTPClient>(),
                    sp.GetRequiredService<IContentProcessor>(),
                    contentStore,
                    sp.GetRequiredService<IEmbeddingGenerator<string,Embedding<float>>>(),
                    sp.GetRequiredService<ILogger<RepositoryIndexer>>()
                );
            });
        // Register our LSP server
        services.AddSingleton<ArchAngelLanguageServer>();
        services.AddSingleton<IContentStoreProvider,ContentStoreProvider>();
        services.AddSingleton<IChatService,FoundryChatService>();
        services.AddSingleton<ICompletionService,FoundryCompletionService>();
        services.AddSingleton<IDocumentGenerationService, SKDocumentGenerationService>();
        services.AddSingleton<IWorkspaceFileService, WorkspaceFileService>();
        
        
    } 
).Build();

// Get the LSP server and start it directly
var lspServer = host.Services.GetRequiredService<ArchAngelLanguageServer>();

// Pre-warm Azure OpenAI credential on background thread
_ = Task.Run(async () =>
{
    try
    {
        Console.Error.WriteLine("[DEBUG] Background pre-warming Azure OpenAI credential...");
        var embeddingService = host.Services.GetRequiredService<IEmbeddingGenerator<string,Embedding<float>>>();
        
        await embeddingService.GenerateAsync("warmup");
        
        Console.Error.WriteLine("[DEBUG] ✅ Azure OpenAI credential pre-warmed successfully");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[DEBUG] ⚠️ Pre-warm failed: {ex.Message}");
    }
});
await lspServer.StartAsync();

