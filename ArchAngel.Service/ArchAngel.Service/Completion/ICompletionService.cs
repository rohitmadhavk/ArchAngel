using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace ArchAngel.Service.Completion;
public interface ICompletionService
{
    Task<CompletionItem[]?> GetCompletionsAsync(string uri,
        int line,
        int character,
        string content);
    Task<CompletionItem> ResolveCompletionAsync(CompletionItem item);
    
}