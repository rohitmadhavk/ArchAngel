namespace ArchAngel.Service.Storage;

public interface IContentStoreProvider
{
    void SetWorkspaceRoot(string? workspaceRoot);
    IContentStore GetContentStore();
}