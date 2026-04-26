using System.Text;
using ArchAngel.Contracts;
using ArchAngel.Service.Content;
using ArchAngel.Service.Storage;
using ArchAngel.Service.Utilities;
using Microsoft.SemanticKernel;

namespace ArchAngel.Service.Chat;
public class SKChatService : ChatServiceBase
{

    private readonly Kernel _kernel;

    private readonly ILogger<SKChatService> _logger;


    public SKChatService(
        Kernel kernel,
        ILogger<SKChatService> logger,
        ICitationProcessor citationProcessor,
        IProjectContextBuilder projectContextBuilder,
        GoldenRepoSearchService goldenRepoSearchService)
        : base(citationProcessor, projectContextBuilder, goldenRepoSearchService, logger)
    {
        _kernel = kernel;
        _logger = logger;
        
    }
    

    protected override string BuildRAGPrompt(string message, string currentProjectFiles, string goldenContext)
    {
        var escapedProjectFiles = EscapeSemanticKernelTemplate(currentProjectFiles);
        var escapedGoldenContext = EscapeSemanticKernelTemplate(goldenContext);
        var escapedMessage = EscapeSemanticKernelTemplate(message);

        // Enhanced prompt with citation instructions
        var prompt = string.Format(PromptConfig.RAGPrompt, 
                                    escapedProjectFiles, 
                                    escapedGoldenContext, 
                                    escapedMessage);
        return prompt;
    }

    protected override async Task<string> InvokePromptAsync(string prompt)
    {
        var response = await _kernel.InvokePromptAsync(prompt);
            
        var responseText = response.ToString();
        return responseText;
    }

    private string EscapeSemanticKernelTemplate(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        try
        {
            // Escape Semantic Kernel template syntax by replacing {{ and }} with safe alternatives
            return text
                .Replace("{{", "﹛﹛")  // Unicode similar characters
                .Replace("}}", "﹜﹜")  // Unicode similar characters
                .Replace("{", "﹛")    // Escape single braces too
                .Replace("}", "﹜");   // Escape single braces too
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[WARNING] Error escaping template syntax: {ex.Message}");
            return text.Replace("{", "").Replace("}", "");
        }
    }
}