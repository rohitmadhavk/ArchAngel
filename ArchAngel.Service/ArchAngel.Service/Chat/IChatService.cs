using ArchAngel.Contracts;
namespace ArchAngel.Service.Chat;
public interface IChatService
{
    Task<ArchAngelChatResponse> ChatAsync(string message, Dictionary<string, string> sessionDocuments, string? codeContext = null, string? filePath = null);

    Task<ArchAngelChatResponse> ChatWithRAGAsync(string message, bool? includeRAG, string? filePath, Dictionary<string, string> sessionDocuments);

}