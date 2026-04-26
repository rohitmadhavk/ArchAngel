public interface IWorkspaceFileService
{
    public Task<Dictionary<string,string>> AutoLoadWorkspaceFilesAsync(string _workspaceRoot);
}