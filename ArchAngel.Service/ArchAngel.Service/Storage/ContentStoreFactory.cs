using ArchAngel.Service.Content;
using ArchAngel.Service.Services;
using Microsoft.Extensions.AI;

namespace ArchAngel.Service.Storage;

/// <summary>
/// Factory for creating appropriate content store based on configuration
/// </summary>
public static class ContentStoreFactory
{
    public static IContentStore CreateContentStore(
        IServiceProvider serviceProvider,
        string? workspaceRoot = null)
    {
        var logger = serviceProvider.GetRequiredService<ILogger<WorkspaceContentStore>>();
        
        if (string.IsNullOrEmpty(workspaceRoot) || !Directory.Exists(workspaceRoot))
        {
            // Use temp directory as fallback instead of broken MemoryContentStore
            workspaceRoot = Path.Combine(Path.GetTempPath(), "archangel");
            Directory.CreateDirectory(workspaceRoot);
            logger.LogWarning("No workspace root provided, using temp: {Path}", workspaceRoot);
        }

        return new WorkspaceContentStore(logger, workspaceRoot, embeddingGenerator: serviceProvider.GetRequiredService<IEmbeddingGenerator<string,Embedding<float>>>());
    }
}