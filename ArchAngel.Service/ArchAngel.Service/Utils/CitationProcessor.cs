using ArchAngel.Contracts;
using ArchAngel.Service.Content;

namespace ArchAngel.Service.Utilities;
class CitationProcessor : ICitationProcessor
{
    public string PostProcessCitations(string responseText, List<ContentSource> sources)
    {
        if (!sources.Any()) return responseText;

        // Replace citation numbers with clickable links (markdown format)
        foreach (var source in sources)
        {
            if (source.citationId.HasValue)
            {
                var citationPattern = $"\\[(?:CITE:)?{source.citationId.Value}\\]";
                var replacement = $"[{source.filePath}]({source.githubUrl ?? "#"})";
                responseText = System.Text.RegularExpressions.Regex.Replace(
                    responseText, 
                    citationPattern, 
                    replacement
                );
            }
        }
        return responseText;
    }
    public string GenerateGitHubUrl(ContentChunk chunk)
    {
        if (chunk.Metadata.TryGetValue("repository", out var repoObj) && repoObj?.ToString() is string repo)
        {
            // Parse repository key like "owner/name:branch"
            var repoParts = repo.Split(':');
            if (repoParts.Length >= 1)
            {
                var ownerName = repoParts[0]; // "owner/name"
                var branch = repoParts.Length > 1 ? repoParts[1] : "main";
                
                return $"https://github.com/{ownerName}/blob/{branch}/{chunk.FilePath}#L{chunk.StartLine}-L{chunk.EndLine}";
            }
        }
        
        return "";
    }
}