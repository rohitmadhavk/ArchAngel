using ArchAngel.Service.Github;
using ArchAngel.Service.Content;
using ArchAngel.Service.Storage;
using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Threading.Channels;
using Microsoft.VisualBasic;
using Microsoft.Extensions.AI;

namespace ArchAngel.Service.Services;

/// <summary>
/// Orchestrates the repository indexing process
/// </summary>
public class RepositoryIndexer : IRepositoryIndexer
{
    private readonly IGithubHTTPClient _githubClient;
    private readonly IContentProcessor _contentProcessor;
    private readonly IContentStore _contentStore;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingService;
    private readonly ILogger<RepositoryIndexer> _logger;
    
    // Track ongoing indexing operations
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeIndexing = new();
    private readonly ConcurrentDictionary<string, IndexingProgress> _indexingProgress = new();

    public RepositoryIndexer(
        IGithubHTTPClient githubClient,
        IContentProcessor contentProcessor,
        IContentStore contentStore,
        IEmbeddingGenerator<string, Embedding<float>> embeddingService,
        ILogger<RepositoryIndexer> logger)
    {
        _githubClient = githubClient;
        _contentProcessor = contentProcessor;
        _contentStore = contentStore;
        _embeddingService = embeddingService;
        _logger = logger;
    }


    public async Task<IndexingResult> IndexRepositoryAsync(string owner, string name, string? branch = null, CancellationToken cancellationToken = default)
    {
        var repositoryKey = $"{owner}/{name}:{branch ?? "main"}";
        var stopwatch = Stopwatch.StartNew();
        

        // Process files in batches
        
        try
        {
            _logger.LogInformation("Starting indexing for repository {Repository}", repositoryKey);
            
            // Check if already indexing
            if (_activeIndexing.ContainsKey(repositoryKey))
            {
                return new IndexingResult(false, "Repository is already being indexed", 0, 0, TimeSpan.Zero);
            }
            await _contentStore.CleanChunksAsync(repositoryKey);
            // Create cancellation token source for this operation
            using var indexingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _activeIndexing[repositoryKey] = indexingCts;
            
            try
            {
                // Step 1: Get repository information
                var repository = await _githubClient.GetRepositoryAsync(owner, name);
                var targetBranch = branch ?? repository.DefaultBranch;
                
                _logger.LogDebug("Repository info: {FullName}, default branch: {DefaultBranch}, size: {Size}KB", 
                    repository.FullName, repository.DefaultBranch, repository.Size);
                
                // Step 2: Get repository tree
                var tree = await _githubClient.GetTreeAsync(owner, name, targetBranch);

                
                if (tree.Truncated)
                {
                    _logger.LogWarning("Repository tree is truncated - some files may be missed");
                }
                
                // Step 3: Filter processable files
                var processableFiles = tree.Tree
                    .Where(item => item.Type == "blob" && 
                                  _contentProcessor.ShouldProcessFile(item.Path, item.Size))
                    .ToList();
                
                _logger.LogInformation("Found {ProcessableFiles} processable files out of {TotalFiles} total files", 
                    processableFiles.Count, tree.Tree.Length);
                
                // Initialize progress tracking
                var progress = new IndexingProgress(
                    RepositoryKey: repositoryKey,
                    TotalFiles: processableFiles.Count,
                    ProcessedFiles: 0,
                    CurrentChunks: 0,
                    CurrentFile: "",
                    StartTime: DateTime.UtcNow,
                    IsCompleted: false
                );
                _indexingProgress[repositoryKey] = progress;
                
                // Step 4: Process files in batches
                var allChunks = new List<ContentChunk>();
                var processedFiles = 0;
                // var batchSize = 10; // Process 10 files concurrently
                var batchSize = Environment.ProcessorCount * 2;
                using var sem = new SemaphoreSlim(batchSize);
                var streamingBufLimit = 50;
                var totalChunks = 0;
                var chunkBuffer = new List<ContentChunk>();
                
                for (int i = 0; i < processableFiles.Count; i += batchSize)
                {
                    indexingCts.Token.ThrowIfCancellationRequested();
                    await Task.Yield();
                    var batch = processableFiles.Skip(i).Take(batchSize);
                    var batchTasks = batch.Select(async file =>
                    {
                        await sem.WaitAsync(indexingCts.Token);
                        try
                        {
                            var res = await ProcessFileAsync(owner, name, file, repositoryKey, indexingCts.Token);
                            var resVal = false;
                            if (res != null)
                            {
                                resVal = true;
                            }
                            _logger.LogError("{File} processed: {res} generated. Type {t}", file, res, resVal);
                            return res;
                        } finally
                        {
                            sem.Release();
                        }
                    });

                    var batchResults = await Task.WhenAll(batchTasks);
                    
                    var batchChunks = batchResults.Where(r => r != null).SelectMany(r => r!).ToList();
                    _logger.LogInformation("Batch complete: {ChunkCount} chunks generated", batchChunks.Count);
                    
                    
                    // allChunks.AddRange(batchChunks);
                    chunkBuffer.AddRange(batchChunks);
                    processedFiles += batch.Count();
                    _logger.LogInformation("Chunk Buffer Size: {ChunkCount} chunks generated", chunkBuffer.Count);
                    var batchChunksStr = string.Join(", ",chunkBuffer);
                    _logger.LogInformation("Chunk Buffer: {chunks} chunks generated", batchChunksStr);
                    if (chunkBuffer.Count >= streamingBufLimit)
                    {
                        var finalRepositoryKey = $"{owner}/{name}:{targetBranch}";
                        await _contentStore.StoreChunksAsync(finalRepositoryKey, chunkBuffer);
                        totalChunks += chunkBuffer.Count;
                        
                        _logger.LogDebug("💾 Streamed {ChunkCount} chunks to storage (total: {TotalChunks})", 
                            chunkBuffer.Count, totalChunks);
                        
                        chunkBuffer.Clear(); 
                    }
                    
                    // Update progress
                    _indexingProgress[repositoryKey] = progress with 
                    { 
                        ProcessedFiles = processedFiles,
                        CurrentChunks = totalChunks + chunkBuffer.Count,
                        CurrentFile = batch.LastOrDefault()?.Path ?? ""
                    };
                    
                    // _logger.LogDebug("Processed batch {BatchStart}-{BatchEnd}, total chunks: {TotalChunks}", 
                    //     i + 1, Math.Min(i + batchSize, processableFiles.Count), allChunks.Count);
                    _logger.LogDebug("📊 Progress: {ProcessedFiles}/{TotalFiles} files, {TotalChunks} chunks stored", 
                        processedFiles, processableFiles.Count, totalChunks);
                    
                    // Rate limiting between batches
                    await Task.Delay(500, indexingCts.Token);
                }
                if (chunkBuffer.Any())
                {
                    var finalRepositoryKey = $"{owner}/{name}:{targetBranch}";
                    await _contentStore.StoreChunksAsync(finalRepositoryKey, chunkBuffer);
                    totalChunks += chunkBuffer.Count;
                    
                    _logger.LogDebug("💾 Stored final {ChunkCount} chunks (total: {TotalChunks})", 
                        chunkBuffer.Count, totalChunks);
                }
                // Step 5: Store all chunks
                // var finalRepositoryKey = $"{owner}/{name}:{targetBranch}";
                // await _contentStore.StoreChunksAsync(finalRepositoryKey, allChunks);
                
                // Mark as completed
                _indexingProgress[repositoryKey] = progress with 
                { 
                    ProcessedFiles = processedFiles,
                    CurrentChunks = totalChunks,
                    IsCompleted = true 
                };
                
                stopwatch.Stop();
                _logger.LogInformation("Successfully indexed {Repository}: {ProcessedFiles} files, {TotalChunks} chunks in {Duration}ms", 
                    repositoryKey, processedFiles, allChunks.Count, stopwatch.ElapsedMilliseconds);
                
                return new IndexingResult(
                    Success: true,
                    Message: $"Successfully indexed {processedFiles} files",
                    ProcessedFiles: processedFiles,
                    TotalChunks: totalChunks,
                    Duration: stopwatch.Elapsed
                );
            }
            finally
            {
                // Cleanup
                _activeIndexing.TryRemove(repositoryKey, out _);
                
                // Keep progress for a while for status queries
                _ = Task.Delay(TimeSpan.FromMinutes(5)).ContinueWith(t => 
                    _indexingProgress.TryRemove(repositoryKey, out _));
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Indexing cancelled for repository {Repository}", repositoryKey);
            return new IndexingResult(false, "Indexing was cancelled", 0, 0, stopwatch.Elapsed);
        }
        catch (GitHubApiException ex)
        {
            _logger.LogError(ex, "GitHub API error while indexing {Repository}", repositoryKey);
            return new IndexingResult(false, $"GitHub API error: {ex.ErrorMessage}", 0, 0, stopwatch.Elapsed, ex.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while indexing {Repository}", repositoryKey);
            return new IndexingResult(false, $"Indexing failed: {ex.Message}", 0, 0, stopwatch.Elapsed, ex.ToString());
        }
    }

    private async Task<List<ContentChunk>?> ProcessFileAsync(string owner, string name, GitHubTreeItem file, string repositoryKey, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();

            
            // Get file content
            var blob = await _githubClient.GetBlobAsync(owner, name, file.Sha);
            
            // Decode content
            var content = _contentProcessor.DecodeContent(blob.Content, blob.Encoding);
            
            // Get language
            var language = _contentProcessor.GetLanguageFromPath(file.Path);
            
            // Process file into chunks
            var chunks = await _contentProcessor.ProcessFileAsync(file.Path, content, language);
            
            // Create new chunks with additional repository metadata
            var enrichedChunks = new List<ContentChunk>();
            
            foreach (var chunk in chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();

                // Generate embedding for this chunk
                float[]? embedding = null;
                try
                {
                    // embedding = await _embeddingService.GenerateEmbeddingAsync(chunk.Content);
                    _logger.LogDebug("Generating embedding");
                    
                    var embeddingP = await _embeddingService.GenerateAsync(chunk.Content);
                    embedding = embeddingP.Vector.ToArray();
                    _logger.LogDebug("✅ Generated embedding for {FilePath} chunk {ChunkIndex}", 
                        file.Path, chunk.ChunkIndex);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Failed to generate embedding for {FilePath} chunk {ChunkIndex}", 
                        file.Path, chunk.ChunkIndex);
                    // Continue without embedding - hybrid search will fall back to keyword
                }
                
                // Create new metadata dictionary with existing metadata plus repository info
                var enrichedMetadata = new Dictionary<string, object>(chunk.Metadata)
                {
                    ["repository"] = repositoryKey,
                    ["sha"] = file.Sha,
                    ["url"] = file.Url,
                    ["mode"] = file.Mode,
                    ["blobSize"] = blob.Size
                };
                
                // Create new chunk with enriched metadata, repository-specific ID, and embedding
                var repositorySpecificId = $"{repositoryKey}:{chunk.Id}";
                var enrichedChunk = chunk with 
                { 
                    Id = repositorySpecificId,
                    Metadata = enrichedMetadata,
                    Embedding = embedding
                };
                
                enrichedChunks.Add(enrichedChunk);
            }
            
            return enrichedChunks;
        }
        catch (OperationCanceledException)
        {
            throw; // Re-throw cancellation
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process file {FilePath} in {Repository}", file.Path, repositoryKey);
            return null; // Return null for failed files, don't fail entire operation
        }
    }

    public Task<IndexingProgress?> GetIndexingProgressAsync(string repositoryKey)
    {
        _indexingProgress.TryGetValue(repositoryKey, out var progress);
        return Task.FromResult(progress);
    }

    public Task CancelIndexingAsync(string repositoryKey)
    {
        if (_activeIndexing.TryGetValue(repositoryKey, out var cts))
        {
            cts.Cancel();
            _logger.LogInformation("Cancellation requested for repository indexing: {Repository}", repositoryKey);
        }
        
        return Task.CompletedTask;
    }
}