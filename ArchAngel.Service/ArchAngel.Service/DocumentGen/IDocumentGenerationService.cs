using ArchAngel.Contracts;

namespace ArchAngel.Service.DocumentGen;
public interface IDocumentGenerationService
{
    public Task<GeneratedDocResponse> GenerateCodeStyleDocumentAsync(string docPath, string fileName);
    public Task<GeneratedDocResponse> GenerateWikiDocumentAsync(string docPath, string fileName);
}