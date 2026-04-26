namespace ArchAngel.Service.Storage;

public class ContentStoreProvider : IContentStoreProvider
{
    private readonly IServiceProvider _serviceProvider;
    private readonly object _lock = new();
    private IContentStore? _contentStore;
    private string? _workspaceRoot;

    public ContentStoreProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public void SetWorkspaceRoot(string? workspaceRoot)
    {
        lock (_lock)
        {
            if (_workspaceRoot != workspaceRoot)
            {
                _workspaceRoot = workspaceRoot;
                _contentStore = null;
            }
        }
    }

    public IContentStore GetContentStore()
    {
        lock (_lock)
        {
            _contentStore ??= ContentStoreFactory.CreateContentStore(_serviceProvider, _workspaceRoot);
            return _contentStore;
        }
    }
}