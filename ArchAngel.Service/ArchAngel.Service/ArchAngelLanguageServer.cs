using Microsoft.SemanticKernel;
using ArchAngel.Contracts;
using StreamJsonRpc;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using System.Text.Json;
using LSPRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;
using ArchAngel.Service.Services;
using ArchAngel.Service.Github;
using ArchAngel.Service.Storage;
using System.Diagnostics;
using ArchAngel.Service.Chat;
using Newtonsoft.Json.Linq;
using ArchAngel.Service.Utilities;
using ArchAngel.Service.Completion;
using ArchAngel.Service.DocumentGen;

/// <summary>
/// LSP server for the ArchAngel VS Code extension. Handles JSON-RPC communication
/// over stdio, providing chat, code completion, repository indexing, and document
/// generation capabilities backed by Azure AI services.
/// </summary>
public class ArchAngelLanguageServer
{
    private readonly ILogger<ArchAngelLanguageServer> _logger;
    private readonly IContentStoreProvider _contentStoreProvider;
    private readonly IGithubHTTPClient _githubHTTPClient;

    private readonly IChatService _chatService;
    private readonly ArchAngel.Service.Configuration.ConfigurationManager _configManager;
    private JsonRpc? _jsonRpc;
    private IRepositoryIndexer? _repositoryIndexer;
    private string? _workspaceRoot;
    private readonly Dictionary<string, string> _sessionDocuments = new();
    private readonly Func<IContentStore, IRepositoryIndexer> _repositoryIndexerFactory;
    private readonly ICompletionService _completionService;
    private readonly IDocumentGenerationService _documentGenerationService;
    private readonly IWorkspaceFileService _workspaceFileService;


    public ArchAngelLanguageServer(
        ILogger<ArchAngelLanguageServer> logger,
        IGithubHTTPClient githubHTTPClient,
        IChatService chatService,
        IDocumentGenerationService documentGenerationService,
        ICompletionService completionService,
        IContentStoreProvider contentStoreProvider,
        Func<IContentStore, IRepositoryIndexer> repositoryIndexerFactory,
        IWorkspaceFileService workspaceFileService,
        ArchAngel.Service.Configuration.ConfigurationManager configManager)
    {
        _logger = logger;
        _chatService = chatService;
        _documentGenerationService = documentGenerationService;
        _completionService = completionService;
        _githubHTTPClient = githubHTTPClient;
        _contentStoreProvider = contentStoreProvider;
        _repositoryIndexerFactory = repositoryIndexerFactory;
        _workspaceFileService = workspaceFileService;
        _configManager = configManager;
    }

    private IRepositoryIndexer GetOrCreateRepositoryIndexer()
    {
        if (_repositoryIndexer == null)
        {
            _repositoryIndexer = _repositoryIndexerFactory(GetOrCreateContentStore());
        }
        return _repositoryIndexer;
    }

    private IContentStore GetOrCreateContentStore()
     => _contentStoreProvider.GetContentStore();



    /// <summary>
    /// Starts the language server, listening for JSON-RPC messages on stdin/stdout.
    /// Blocks until the connection is closed or an error occurs.
    /// </summary>
    public async Task StartAsync()
    {
        _logger.LogDebug("[DEBUG] Starting ArchAngel Language Server...");

        try
        {
            var stdout = Console.OpenStandardOutput();
            var stdin = Console.OpenStandardInput();

            var formatter = new JsonMessageFormatter();
            var messageHandler = new HeaderDelimitedMessageHandler(stdout, stdin, formatter);

            _jsonRpc = new JsonRpc(messageHandler, this);

            _jsonRpc.AllowModificationWhileListening = true;

            _jsonRpc.Disconnected += (sender, e) =>
            {
                _logger.LogDebug($"[DEBUG] JsonRpc disconnected: {e.Reason}");
            };

            // This is to log all incoming requests/notifications in DEBUG
            #if DEBUG
            _jsonRpc.TraceSource = new TraceSource("JsonRpc");
            _jsonRpc.TraceSource.Switch = new SourceSwitch("JsonRpcSwitch", "Warning");
            var traceListener = new TextWriterTraceListener(Console.Error);
            _jsonRpc.TraceSource.Listeners.Add(traceListener);
            #endif

            _logger.LogDebug("[DEBUG] JsonRpc created, starting listener...");
            _jsonRpc.StartListening();

            _logger.LogDebug("[DEBUG] JsonRpc listening started successfully");
            
            await _jsonRpc.Completion;
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[DEBUG] StartAsync failed: {ex}");
            throw;
        }
    }

    
    /// <summary>
    /// Handles the LSP 'initialize' request. Sets up the workspace root, initializes
    /// the content store, and returns server capabilities (text sync, completion, hover).
    /// </summary>
    [JsonRpcMethod("initialize")]
    public Task<InitializeResult> Initialize(
        int? processId = null,
        string? rootPath = null,
        string? rootUri = null,
        object? initializationOptions = null,
        object? capabilities = null,
        string? trace = null,
        object? workspaceFolders = null,
        object? clientInfo = null,
        object? locale = null) 
    {
        Console.Error.WriteLine("🚀 INITIALIZE CALLED!");
        _logger.LogDebug($"[DEBUG] Process ID: {processId}");
        _logger.LogDebug($"[DEBUG] Root path: {rootPath ?? "NULL"}");
        _logger.LogDebug($"[DEBUG] Root URI: {rootUri ?? "NULL"}");
        
        // Initialize workspace-specific content store
        if (rootUri != null && Uri.TryCreate(rootUri, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            // Properly convert URI to local path on Windows
            var localPath = uri.LocalPath;
            _logger.LogDebug($"[DEBUG] Raw LocalPath: {localPath}");
            
            // Handle Windows drive letter paths that start with '/'
            if (localPath.StartsWith("/") && localPath.Length > 3 && localPath[2] == ':')
            {
                localPath = localPath.Substring(1); // Remove leading '/' from '/c:/Users/...'
            }
            
            // Convert forward slashes to backslashes on Windows
            _workspaceRoot = localPath.Replace('/', '\\');
            
            _logger.LogDebug($"[DEBUG] Workspace path normalized: {_workspaceRoot}");
            _logger.LogDebug($"[DEBUG] Directory exists check: {Directory.Exists(_workspaceRoot)}");
            Console.Error.WriteLine($"[INFO] 💾 Initialized persistent storage for workspace: {_workspaceRoot}");
        }
        else if (!string.IsNullOrEmpty(rootPath))
        {
            _workspaceRoot = rootPath;
            Console.Error.WriteLine($"[INFO] 💾 Initialized persistent storage for workspace: {_workspaceRoot}");
        }
        else
        {
            Console.Error.WriteLine("[WARN] ⚠️ No workspace detected - content store will use temp directory");
        }
        _contentStoreProvider.SetWorkspaceRoot(_workspaceRoot);
        _logger.LogDebug($"[DEBUG] Client capabilities: {capabilities?.ToString() ?? "NULL"}");
        _logger.LogDebug($"[DEBUG] Trace: {trace ?? "NULL"}");
        _logger.LogDebug($"[DEBUG] Workspace folders: {workspaceFolders?.ToString() ?? "NULL"}");
        _logger.LogDebug($"[DEBUG] Client info: {clientInfo?.ToString() ?? "NULL"}");
        _logger.LogDebug($"[DEBUG] Locale: {locale?.ToString() ?? "NULL"}");

        // Auto-load key workspace files into session for immediate availability
        if (!string.IsNullOrEmpty(_workspaceRoot) && Directory.Exists(_workspaceRoot))
        {
            Task.Run(async () => await AutoLoadWorkspaceFilesAsync());
        }
        
        var result = new InitializeResult
        {
            Capabilities = new ServerCapabilities
            {
                TextDocumentSync = new TextDocumentSyncOptions
                {
                    OpenClose = true,
                    Change = TextDocumentSyncKind.Full
                },
                CompletionProvider = new CompletionOptions
                {
                    TriggerCharacters = [".", " ", "(", "{", "["],
                    ResolveProvider = true
                },
            }
        };
        
        _logger.LogDebug("[DEBUG] 📤 ADVERTISING SERVER CAPABILITIES:");
        _logger.LogDebug($"[DEBUG]   - TextDocumentSync.OpenClose: {result.Capabilities.TextDocumentSync?.OpenClose}");
        _logger.LogDebug($"[DEBUG]   - TextDocumentSync.Change: {result.Capabilities.TextDocumentSync?.Change}");
        _logger.LogDebug($"[DEBUG]   - CompletionProvider: {result.Capabilities.CompletionProvider != null}");
        _logger.LogDebug("[DEBUG] ✅ Initialize should complete successfully now!");
        _ = GetStorageInfoAsync();
        
        return Task.FromResult(result);
    }

    /// <summary>
    /// Handles the LSP 'initialized' notification. No-op; the server is already ready after initialize.
    /// </summary>
    [JsonRpcMethod("initialized")]
    public Task Initialized(InitializedParams @params)
    {
        // LSP Initialized - server is ready
        return Task.CompletedTask;
    }

    /// <summary>
    /// No-op keep-alive used by the client to pump the JSON-RPC connection during long-running operations.
    /// </summary>
    [JsonRpcMethod("archAngel/rpcPrompt")]
    public Task RPCPrompt()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns storage statistics including database path, size, repository count,
    /// chunk count, and language distribution for the current workspace's knowledge base.
    /// </summary>
    [JsonRpcMethod("archAngel/getStorageInfo")]
    public async Task<object> GetStorageInfoAsync()
    {
        try
        {
            var contentStore = GetOrCreateContentStore();
            var stats = await contentStore.GetStatsAsync();
            
            var dbSize = contentStore.GetDatabaseSizeAsync();
            var res = new
            {
                type = "persistent",
                workspaceRoot = contentStore.GetWorkspaceRoot(),
                databasePath = contentStore.GetDatabasePath(),
                databaseSize = dbSize,
                databaseSizeMB = Math.Round(dbSize / 1024.0 / 1024.0, 2),
                statistics = new
                {
                    totalRepositories = stats.TotalRepositories,
                    totalChunks = stats.TotalChunks,
                    totalContentSize = stats.TotalContentSize,
                    totalContentSizeMB = Math.Round(stats.TotalContentSize / 1024.0 / 1024.0, 2),
                    languageDistribution = "{" + string.Join(", ", stats.LanguageDistribution.Select(kv => $"{kv.Key}={kv.Value}")) + "}",
                    lastUpdated = stats.LastUpdated,
                    files = string.Join(", ",stats.files)
                }
            };
            _logger.LogDebug($"[DEBUG] Found {res} indexed repositories:");
            return res;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get storage info");
            return new { error = ex.Message };
        }
    }

    /// <summary>
    /// Reads and returns the ArchAngel configuration (archangel.json) for the current workspace,
    /// including the list of golden repositories to index.
    /// </summary>
    [JsonRpcMethod("archAngel/getConfiguration")]
    public async Task<object> GetConfigurationAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(_workspaceRoot))
            {
                return new { error = "No workspace available" };
            }

            var config = await _configManager.ResolveConfigurationAsync(_workspaceRoot);
            return new
            {
                name = config.Name,
                version = config.Version,
                repositories = config.Repositories,
                workspaceRoot = _workspaceRoot
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get configuration");
            return new { error = ex.Message };
        }
    }

    /// <summary>
    /// Persists updated ArchAngel configuration to the workspace's archangel.json file.
    /// </summary>
    [JsonRpcMethod("archAngel/saveConfiguration")]
    public async Task<object> SaveConfigurationAsync(object configData)
    {
        try
        {
            if (string.IsNullOrEmpty(_workspaceRoot))
            {
                return new { success = false, error = "No workspace available" };
            }

            var json = JsonSerializer.Serialize(configData);
            var config = JsonSerializer.Deserialize<ArchAngel.Service.Configuration.ArchAngelConfig>(json);
            
            if (config != null)
            {
                await _configManager.SaveConfigurationAsync(_workspaceRoot, config);
                return new { success = true };
            }

            return new { success = false, error = "Invalid configuration data" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save configuration");
            return new { success = false, error = ex.Message };
        }
    }

    /// <summary>
    /// Handles the LSP 'shutdown' request. Signals the server to prepare for exit.
    /// </summary>
    [JsonRpcMethod("shutdown")]
    public object? Shutdown()
    {
        _logger.LogInformation("LSP Shutdown called");
        return null;
    }

    /// <summary>
    /// Handles the LSP 'exit' notification. Disposes the JSON-RPC connection and terminates the process.
    /// </summary>
    [JsonRpcMethod("exit")]
    public void Exit()
    {
        _logger.LogInformation("LSP Exit called");
        _jsonRpc?.Dispose();
        Environment.Exit(0);
    }


    /// <summary>
    /// Returns the list of repository keys currently indexed in the knowledge base.
    /// </summary>
    [JsonRpcMethod("archAngel/getIndexedRepositories")]
    public async Task<object> GetIndexedRepositoriesAsync()
    {
        try
        {
            var contentStore = GetOrCreateContentStore();
            var indexedRepos = await contentStore.GetIndexedRepositoriesAsync();
            
            _logger.LogDebug("Found {RepositoryCount} indexed repositories", indexedRepos.Count);
            
            return new
            {
                success = true,
                repositories = indexedRepos
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get indexed repositories");
            return new
            {
                success = false,
                error = ex.Message,
                repositories = new string[0]
            };
        }
    }

    

   
    /// <summary>
    /// Indexes a GitHub repository into the knowledge base. Fetches files via the GitHub API,
    /// chunks and embeds content, and stores results in the SQLite vector store.
    /// Runs on a background thread to avoid blocking the JSON-RPC message loop.
    /// </summary>
    /// <param name="owner">GitHub repository owner.</param>
    /// <param name="name">GitHub repository name.</param>
    /// <param name="branch">Branch to index. Defaults to the repository's default branch.</param>
    [JsonRpcMethod("archAngel/indexRepository")]
    public Task<IndexRepositoryResponse> IndexRepositoryAsync(string owner, string name, string? branch = null)
    {
        
        return Task.Run(async () =>
        {
            
            SynchronizationContext.SetSynchronizationContext(null);
            var res = await IndexRepositoryInternalAsync(owner, name, branch).ConfigureAwait(false);
            return res;
        });
    }

    private async Task<IndexRepositoryResponse> IndexRepositoryInternalAsync(string owner, string name, string? branch)
    {
        
        _logger.LogDebug($"[DEBUG] IndexRepositoryAsync method called!");
        _logger.LogDebug($"[DEBUG] Repository: {owner}/{name}, Branch: {branch ?? "default"}");

        try
        {
            
            
            if (_githubHTTPClient.IsAuthenticated)
            {
                _logger.Log(LogLevel.Debug, "[DEBUG] Git Auth OK!");
            }
            else
            {
                Console.Error.WriteLine("[WARNING] No GitHub authentication found - using anonymous access (rate limited)");
            }
            // Create a progress tracking task
            var repositoryKey = $"{owner}/{name}:{branch ?? "default"}";
            var progressTask = Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(2000); // Check every 2 seconds
                    try
                    {
                        var progress = await GetOrCreateRepositoryIndexer().GetIndexingProgressAsync(repositoryKey);
                        if (progress != null && !progress.IsCompleted)
                        {
                            var percentage = progress.TotalFiles > 0 ? (double)progress.ProcessedFiles / progress.TotalFiles * 100 : 0;
                            Console.Error.WriteLine($"[PROGRESS] 📊 {percentage:F1}% - {progress.ProcessedFiles}/{progress.TotalFiles} files, {progress.CurrentChunks} chunks, Current: {progress.CurrentFile}");
                        }
                        else if (progress?.IsCompleted == true)
                        {
                            Console.Error.WriteLine($"[PROGRESS] ✅ Indexing completed!");
                            break;
                        }
                    }
                    catch
                    {
                        // Progress tracking failed, continue
                        break;
                    }
                }
            });

            var result = await GetOrCreateRepositoryIndexer().IndexRepositoryAsync(owner, name, branch);
            
            // Stop progress tracking
            
            _logger.LogDebug($"[DEBUG] ==================== INDEXING RESULT ====================");
            _logger.LogDebug($"[DEBUG] Success: {result.Success}");
            _logger.LogDebug($"[DEBUG] Message: {result.Message}");
            _logger.LogDebug($"[DEBUG] Files Processed: {result.ProcessedFiles}");
            _logger.LogDebug($"[DEBUG] Total Chunks: {result.TotalChunks}");
            _logger.LogDebug($"[DEBUG] Duration: {result.Duration}");
            _logger.LogDebug($"[DEBUG] ==================== INDEXING END ====================");            
            _logger.LogDebug($"[DEBUG] Indexing result: Success={result.Success}, Files={result.ProcessedFiles}, Chunks={result.TotalChunks}");
            
            return new IndexRepositoryResponse(
                success: result.Success,
                message: result.Message,
                indexedFiles: result.ProcessedFiles
            );
        }
        catch (Exception ex)
        {
            _logger.LogError($"[ERROR] Failed to index repository: {ex.Message}");
            _logger.LogError(ex, "Repository indexing failed for {Owner}/{Name}", owner, name);
            
            return new IndexRepositoryResponse(
                success: false,
                message: $"Failed to index repository {owner}/{name}: {ex.Message}",
                indexedFiles: null
            );
        }
    }

    /// <summary>
    /// Indexes all repositories listed in the workspace's archangel.json configuration file.
    /// </summary>
    [JsonRpcMethod("archAngel/indexFromConfig")]
    public Task<object> IndexFromConfigAsync()
    {
        return Task.Run(async () =>
        {
            SynchronizationContext.SetSynchronizationContext(null);
            return await IndexFromConfigInternalAsync().ConfigureAwait(false);
        });
    }
    private async Task<object> IndexFromConfigInternalAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(_workspaceRoot))
            {
                return new { success = false, error = "No workspace available" };
            }

            var config = await _configManager.ResolveConfigurationAsync(_workspaceRoot);
            
            Console.Error.WriteLine($"[CONFIG] Found {config.Repositories.Count} repositories in config");
            
            var results = new List<object>();
            
            foreach (var repoString in config.Repositories)
            {
                var parts = repoString.Split('/');
                if (parts.Length == 2)
                {
                    var owner = parts[0];
                    var name = parts[1];
                    
                    Console.Error.WriteLine($"[CONFIG] Indexing {owner}/{name} from config...");
                    
                    try
                    {
                        var indexResult = await IndexRepositoryAsync(owner, name);
                        results.Add(new
                        {
                            repository = repoString,
                            success = indexResult.success,
                            message = indexResult.message
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[CONFIG] Failed to index {repoString}: {ex.Message}");
                        results.Add(new
                        {
                            repository = repoString,
                            success = false,
                            message = ex.Message
                        });
                    }
                }
                else
                {
                    Console.Error.WriteLine($"[CONFIG] Invalid repository format: {repoString}");
                    results.Add(new
                    {
                        repository = repoString,
                        success = false,
                        message = "Invalid repository format. Expected 'owner/name'"
                    });
                }
            }
            
            return new
            {
                success = true,
                configName = config.Name,
                indexedCount = results.Count(r => r.GetType().GetProperty("success")?.GetValue(r) is true),
                totalCount = results.Count,
                results
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index repositories from config");
            return new 
            { 
                success = false, 
                error = ex.Message 
            };
        }
    }


    /// <summary>
    /// Returns the current GitHub authentication status and its source (VS Code OAuth or none).
    /// </summary>
    [JsonRpcMethod("archAngel/getAuthStatus")]
    public Task<object> GetAuthStatusAsync()
    {
        return Task.FromResult<object>(new
        {
            authenticated= _githubHTTPClient.IsAuthenticated,
            source = _githubHTTPClient.IsAuthenticated ? "vscode-oauth": "none"
        });

    }

    
    /// <summary>
    /// Returns the progress of an in-flight repository indexing operation,
    /// including processed/total files, current file, and completion percentage.
    /// </summary>
    /// <param name="owner">GitHub repository owner.</param>
    /// <param name="name">GitHub repository name.</param>
    /// <param name="branch">Branch being indexed.</param>
    [JsonRpcMethod("archAngel/getIndexingProgress")]
    public async Task<object> GetIndexingProgressAsync(string owner, string name, string? branch = null)
    {
        try
        {
            var repositoryKey = $"{owner}/{name}:{branch ?? "default"}";
            var progress = await GetOrCreateRepositoryIndexer().GetIndexingProgressAsync(repositoryKey);
            
            if (progress == null)
            {
                return new { exists = false, message = "No indexing operation found for this repository" };
            }
            
            return new 
            { 
                exists = true,
                repositoryKey = progress.RepositoryKey,
                totalFiles = progress.TotalFiles,
                processedFiles = progress.ProcessedFiles,
                currentChunks = progress.CurrentChunks,
                currentFile = progress.CurrentFile,
                startTime = progress.StartTime,
                isCompleted = progress.IsCompleted,
                progressPercentage = progress.TotalFiles > 0 ? (double)progress.ProcessedFiles / progress.TotalFiles * 100 : 0
            };
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, $"[ERROR] Failed to get indexing progress: {ex.Message}");
            return new { exists = false, error = ex.Message };
        }
    }

    /// <summary>
    /// Requests cancellation of an in-flight repository indexing operation.
    /// </summary>
    /// <param name="owner">GitHub repository owner.</param>
    /// <param name="name">GitHub repository name.</param>
    /// <param name="branch">Branch being indexed.</param>
    [JsonRpcMethod("archAngel/cancelIndexing")]
    public async Task<object> CancelIndexingAsync(string owner, string name, string? branch = null)
    {
        try
        {
            var repositoryKey = $"{owner}/{name}:{branch ?? "default"}";
            await GetOrCreateRepositoryIndexer().CancelIndexingAsync(repositoryKey);
            
            return new { success = true, message = $"Cancellation requested for {repositoryKey}" };
        }
        catch (Exception ex)
        {
            _logger.LogError($"[ERROR] Failed to cancel indexing: {ex.Message}");
            return new { success = false, error = ex.Message };
        }
    }
    private async Task AutoLoadWorkspaceFilesAsync()
    {
        if (string.IsNullOrEmpty(_workspaceRoot)) return;

        var loaded = await _workspaceFileService.AutoLoadWorkspaceFilesAsync(_workspaceRoot);
        foreach (var (uri, content) in loaded)
        {
            _sessionDocuments[uri] = content;
        }
    }

    /// <summary>
    /// Handles the LSP textDocument/didOpen notification. Stores the document content
    /// in the in-memory session document cache for use by completion.
    /// </summary>
    [JsonRpcMethod("textDocument/didOpen", UseSingleObjectParameterDeserialization = true)]
    public void DidOpenTextDocument(DidOpenTextDocumentParams @params)
    {
        _logger.LogDebug($"[DEBUG] 📁 DIDOPEN NOTIFICATION RECEIVED!");
        _logger.LogDebug($"[DEBUG] textDocument parameter: {@params.TextDocument}");
        
        try
        {
            // Extract properties from textDocument object            
            if (@params.TextDocument != null)
            {
                try
                {
                    string uri = @params.TextDocument.Uri.ToString();
                    string languageId = @params.TextDocument.LanguageId.ToString();
                    int version = @params.TextDocument.Version;
                    string text = @params.TextDocument.Text.ToString();
                    
                    // URL decode the URI
                    if (!string.IsNullOrEmpty(uri))
                    {
                        uri = System.Web.HttpUtility.UrlDecode(uri);
                    }
                    
                    _logger.LogDebug($"[DEBUG] Extracted - URI: '{uri}', Language: '{languageId}', Version: {version}");
                    _logger.LogDebug($"[DEBUG] Text length: {text?.Length ?? 0}");
                    _logger.LogDebug($"[DEBUG] Text preview: {(text is not null ? text[..Math.Min(100, text.Length)] : "")}");
                    
                    if (!string.IsNullOrEmpty(uri) && text != null)
                    {
                        _sessionDocuments[uri] = text;
                        _logger.LogDebug($"[DEBUG] ✅ Document stored! Total docs: {_sessionDocuments.Count}");
                    }
                    else
                    {
                        _logger.LogDebug($"[DEBUG] ❌ Missing URI or text content");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug($"[DEBUG] ❌ Failed to extract textDocument properties: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[DEBUG] ❌ Failed to process didOpen: {ex.Message}");
        }
    }

    /// <summary>
    /// Sets the GitHub personal access token used for authenticated API requests.
    /// The token is held in memory only and not persisted to disk.
    /// </summary>
    /// <param name="authToken">GitHub PAT or OAuth access token.</param>
    [JsonRpcMethod("archAngel/setAuthToken")]
    public Task<object> setAuthToken(string? authToken)
    {
        if (!string.IsNullOrEmpty(authToken))
        {
            _githubHTTPClient.SetAuthToken(authToken);
            return Task.FromResult<object>(new
            {
                success = true
            });
        } else
        {
            return Task.FromResult<object>(new
            {
                success = false
            });
        }
    }

    /// <summary>
    /// Handles the LSP textDocument/didChange notification. Updates the in-memory
    /// session document cache with the latest document content.
    /// </summary>
    [JsonRpcMethod("textDocument/didChange", UseSingleObjectParameterDeserialization = true)]
    public void DidChangeTextDocument(DidChangeTextDocumentParams @params)
    {
        _logger.LogDebug($"[DEBUG] 📝 DIDCHANGE NOTIFICATION RECEIVED!");
        
        try
        {
            // Extract URI from textDocument
            string uri = @params.TextDocument.Uri.ToString();
            
            // Extract content changes
            if (@params.ContentChanges != null && !string.IsNullOrEmpty(uri))
            {
                try
                {
                    // ContentChanges is an array, get the last (most recent) change
                    dynamic changesDynamic = @params.ContentChanges;
                    
                    // Handle both array and single object cases
                    dynamic lastChange = null;
                    if (changesDynamic is System.Collections.IEnumerable enumerable)
                    {
                        var changesList = enumerable.Cast<object>().ToList();
                        if (changesList.Any())
                        {
                            lastChange = changesList.Last();
                        }
                    }
                    else
                    {
                        lastChange = changesDynamic;
                    }
                    
                    if (lastChange != null)
                    {
                        string? newText = lastChange.text?.ToString();
                        if (newText != null)
                        {
                            _sessionDocuments[uri] = newText;
                            _logger.LogDebug($"[DEBUG] Document updated: {uri} ({newText.Length} chars)");
                            _logger.LogDebug($"[DEBUG] Updated content preview: {newText.Substring(0, Math.Min(100, newText.Length))}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug($"[DEBUG] Failed to extract content changes: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[DEBUG] ❌ Failed to process didChange: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles the LSP textDocument/didClose notification. Removes the document
    /// from the in-memory session document cache.
    /// </summary>
    [JsonRpcMethod("textDocument/didClose", UseSingleObjectParameterDeserialization = true)]
    public void DidCloseTextDocument(DidCloseTextDocumentParams textDocument)
    {
        _logger.LogDebug($"[DEBUG] 📄 DIDCLOSE NOTIFICATION RECEIVED!");

        try
        {
            string? uri = null;
            if (textDocument != null)
            {
                try
                {
                    dynamic textDocDynamic = textDocument;
                    uri = textDocDynamic.uri?.ToString();

                    if (!string.IsNullOrEmpty(uri))
                    {
                        uri = System.Web.HttpUtility.UrlDecode(uri);
                        _sessionDocuments.Remove(uri);
                        _logger.LogDebug($"[DEBUG] Document closed and removed: {uri}");
                        _logger.LogDebug($"[DEBUG] Remaining documents: {_sessionDocuments.Count}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug($"[DEBUG] Failed to extract URI from textDocument: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[DEBUG] ❌ Failed to process didClose: {ex.Message}");
        }
    }

    /// <summary>
    /// Removes a repository and all its indexed chunks from the knowledge base.
    /// </summary>
    /// <param name="owner">GitHub repository owner.</param>
    /// <param name="name">GitHub repository name.</param>
    /// <param name="branch">Branch to remove. Defaults to "main".</param>
    [JsonRpcMethod("archAngel/removeRepository")]
    public async Task<object> RemoveRepositoryAsync(string owner, string name, string? branch = null)
    {
        try
        {
            var repositoryKey = $"{owner}/{name}:{branch ?? "main"}";
            _logger.LogDebug($"[DEBUG] Removing repository from RAG: {repositoryKey}");
            
            // Remove the repository from the content store (RAG)
            await GetOrCreateContentStore().RemoveRepositoryAsync(repositoryKey);
            
            _logger.LogDebug($"[DEBUG] ✅ Repository {repositoryKey} removed from RAG successfully");
            
            return new
            {
                success = true,
                message = $"Repository {repositoryKey} removed successfully",
                repositoryKey = repositoryKey
            };
        }
        catch (Exception ex)
        {
            _logger.LogError($"[ERROR] Failed to remove repository from RAG: {ex.Message}");
            return new
            {
                success = false,
                error = ex.Message,
                repositoryKey = $"{owner}/{name}:{branch ?? "main"}"
            };
        }
    }

    /// <summary>
    /// Handles the LSP completionItem/resolve request. Enriches a completion item
    /// with additional detail (e.g., documentation, insert text) before display.
    /// </summary>
    [JsonRpcMethod("completionItem/resolve", UseSingleObjectParameterDeserialization = true)]
    public Task<CompletionItem> ResolveCompletionAsync(
        CompletionItem completionItem)
    {
        try
        {
            return _completionService.ResolveCompletionAsync(completionItem);
        }
        catch (Exception ex)
        {
            _logger.LogError($"[ERROR] ResolveCompletionAsync failed: {ex.Message}");
            _logger.LogError(ex, "Error in ResolveCompletionAsync");
            
            // Return a basic completion item as fallback
            return Task.FromResult(new CompletionItem { Label = "Error", Kind = CompletionItemKind.Text });
        }
    }

    /// <summary>
    /// Handles the LSP textDocument/completion request. Returns AI-powered code
    /// completion suggestions for the given cursor position.
    /// </summary>
    [JsonRpcMethod("textDocument/completion")]
    public async Task<CompletionList?> CompletionAsync(
        JObject textDocument,
        JObject position,
        JObject? context = null)
    {
        try
        {
            _logger.LogDebug($"[DEBUG] CompletionAsync called with 3 parameters!");
            _logger.LogDebug($"[DEBUG] TextDocument type: {textDocument?.GetType().Name ?? "NULL"}");
            _logger.LogDebug($"[DEBUG] Position type: {position?.GetType().Name ?? "NULL"}");
            _logger.LogDebug($"[DEBUG] Context type: {context?.GetType().Name ?? "NULL"}");
            
            // Let's see the raw objects
            _logger.LogDebug($"[DEBUG] TextDocument raw: {textDocument?.ToString() ?? "NULL"}");
            _logger.LogDebug($"[DEBUG] Position raw: {position?.ToString() ?? "NULL"}");
            
            
            // Try to extract URI from textDocument parameter using reflection/dynamic
            var uri = System.Web.HttpUtility.UrlDecode(textDocument["uri"]?.ToString());
            var line = position["line"]?.Value<int>() ?? 0;
            var character = position["character"]?.Value<int>() ?? 0;

            if (string.IsNullOrEmpty(uri))
            {
                _logger.LogDebug("DidOpen: missing URI");
                return null;
            }
            if (!_sessionDocuments.TryGetValue(uri, out var content))
            {
                _logger.LogDebug("CompletionAsync: no document for {Uri}", uri);
                return null;
            }
            

            var completionItems = await _completionService.GetCompletionsAsync(uri,line,character,content);
            return new CompletionList
                {
                    IsIncomplete = false,
                    Items = completionItems ?? []
                };
            
        }
        catch (Exception ex)
        {
            _logger.LogError($"[ERROR] CompletionAsync failed: {ex.Message}");
            _logger.LogError($"[ERROR] Stack trace: {ex.StackTrace}");
            _logger.LogError(ex, "Error in CompletionAsync");
            return new CompletionList
            {
                IsIncomplete = false,
                Items = []
            };;
        }
    }


    /// <summary>
    /// Simple echo endpoint for testing JSON-RPC connectivity.
    /// </summary>
    [JsonRpcMethod("archAngel/ping")]
    public string Ping(string message)
    {
        _logger.LogDebug($"[DEBUG] Ping method called with message: {message}");
        return $"pong: {message}";
    }


    /// <summary>
    /// Multi-parameter echo endpoint for testing JSON-RPC serialization of complex payloads.
    /// </summary>
    [JsonRpcMethod("archAngel/pingMultiple")]
    public PingMultipleResponse PingMultiple(string message, int number, string? optional = null)
    {
        _logger.LogDebug($"[DEBUG] PingMultiple called - message: {message}, number: {number}, optional: {optional ?? "NULL"}");
        return new PingMultipleResponse(
            $"pong: {message}, {number}, {optional ?? "none"}",
            message,
            number,
            optional,
            DateTime.UtcNow
        );
    }

    /// <summary>
    /// Sends a chat message to the AI service and returns a response.
    /// Includes open session documents as context.
    /// </summary>
    /// <param name="message">The user's chat message.</param>
    /// <param name="codeContext">Optional code snippet to include as context.</param>
    /// <param name="filePath">Optional file path for additional context.</param>
    [JsonRpcMethod("archAngel/chat")]
    public Task<ArchAngelChatResponse> ChatAsync(string message, string? codeContext = null, string? filePath = null)
     => _chatService.ChatAsync(message,_sessionDocuments,codeContext,filePath);

    /// <summary>
    /// Generates a wiki document from the indexed golden repositories and writes it to disk.
    /// </summary>
    /// <param name="docPath">Directory path to write the document to.</param>
    /// <param name="fileName">Base filename (without .md extension).</param>
    [JsonRpcMethod("archAngel/generateWikiDoc")]
    public Task<GeneratedDocResponse> GenerateWikiDocument(string docPath, string fileName)
        => _documentGenerationService.GenerateWikiDocumentAsync(docPath, string.Concat(fileName,".md"));

    /// <summary>
    /// Generates a code style guide document from the indexed golden repositories and writes it to disk.
    /// </summary>
    /// <param name="docPath">Directory path to write the document to.</param>
    /// <param name="fileName">Base filename (without .md extension).</param>
    [JsonRpcMethod("archAngel/generateCodeStyleDoc")]
    public Task<GeneratedDocResponse> GenerateCodeStyleDocument(string docPath, string fileName)
        => _documentGenerationService.GenerateCodeStyleDocumentAsync(docPath, string.Concat(fileName,".md"));

    
    /// <summary>
    /// Sends a chat message with RAG-augmented context from the golden repository knowledge base.
    /// Performs semantic search, injects matching code chunks into the prompt, and returns
    /// a response with citation sources.
    /// </summary>
    /// <param name="message">The user's chat message.</param>
    /// <param name="includeRAG">Whether to include RAG context from the knowledge base.</param>
    /// <param name="filePath">Optional file path for additional context.</param>
    [JsonRpcMethod("archAngel/chatWithRAG")]
    public Task<ArchAngelChatResponse> ChatWithRAGAsync(string message, bool? includeRAG, string? filePath)
     => _chatService.ChatWithRAGAsync(message,includeRAG,filePath, _sessionDocuments);

    
    
    private static string GetWordAtPosition(string line, int position)
    {
        if (position >= line.Length) return string.Empty;

        var start = position;
        var end = position;

        // Find word boundaries
        while (start > 0 && (char.IsLetterOrDigit(line[start - 1]) || line[start - 1] == '_'))
            start--;

        while (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] == '_'))
            end++;

        return end > start ? line.Substring(start, end - start) : string.Empty;
    }

    
    /// <summary>
    /// Wipes all indexed data from the knowledge base, including all repositories and chunks.
    /// Resets the content store and repository indexer.
    /// </summary>
    [JsonRpcMethod("archAngel/wipeKnowledgeBase")]
    public async Task<object> WipeKnowledgeBaseAsync()
    {
        try
        {
            _logger.LogDebug("[DEBUG] Wiping knowledge base...");
            
            if (string.IsNullOrEmpty(_workspaceRoot))
            {
                return new { success = false, error = "No workspace available" };
            }
            
            var contentStore = GetOrCreateContentStore();
            
            
            _logger.LogDebug("[DEBUG] Wiping persistent database contents...");
            
            // Get stats before wiping for logging
            var statsBefore = await contentStore.GetStatsAsync();
            _logger.LogDebug($"[DEBUG] Before wipe: {statsBefore.TotalRepositories} repositories, {statsBefore.TotalChunks} chunks");
            
            // Clear all database contents using the WorkspaceContentStore
            await contentStore.WipeDatabaseAsync();
            
            // Reset in-memory references
            _contentStoreProvider.SetWorkspaceRoot(_workspaceRoot);
            _repositoryIndexer = null;
            
            
            _logger.LogDebug("[DEBUG] Database wiped successfully");
            
            return new 
            { 
                success = true, 
                message = $"Successfully wiped knowledge base. Removed {statsBefore.TotalRepositories} repositories and {statsBefore.TotalChunks} chunks.",
                deletedRepositories = statsBefore.TotalRepositories,
                deletedChunks = statsBefore.TotalChunks,
                method = "database_wipe"
            };
            
        }
        catch (Exception ex)
        {
            _logger.LogError($"[ERROR] Failed to wipe knowledge base: {ex.Message}");
            _logger.LogError($"[ERROR] Stack trace: {ex.StackTrace}");
            return new 
            { 
                success = false, 
                error = ex.Message 
            };
        }
    }
    
}