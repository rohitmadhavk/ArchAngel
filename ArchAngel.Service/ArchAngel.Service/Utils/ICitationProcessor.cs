using ArchAngel.Contracts;
using ArchAngel.Service.Content;

namespace ArchAngel.Service.Utilities;

public interface ICitationProcessor
{
    string PostProcessCitations(string responseText, List<ContentSource> sources);
    string GenerateGitHubUrl(ContentChunk chunk);
}