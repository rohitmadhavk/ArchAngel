using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ArchAngel.Service.Content;
using System.Text.RegularExpressions;
using ArchAngel.Service.Services;
using Microsoft.SemanticKernel.Embeddings;
using System.Numerics;
using Microsoft.Extensions.AI;
using System.Numerics.Tensors;

namespace ArchAngel.Service.Storage;

/// <summary>
/// Workspace-specific SQLite content store that persists to .ca file
/// </summary>
public class WorkspaceContentStore : IContentStore, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ILogger<WorkspaceContentStore> _logger;
    private readonly string _workspaceRoot;
    private readonly string _databasePath;
    // private readonly EmbeddingService _embeddingService;
    private readonly IEmbeddingGenerator<string,Embedding<float>> _embeddingGenerator;

    public WorkspaceContentStore(ILogger<WorkspaceContentStore> logger, string workspaceRoot, IEmbeddingGenerator<string,Embedding<float>> embeddingGenerator)
    {
        _logger = logger;
        _workspaceRoot = workspaceRoot;
        _embeddingGenerator = embeddingGenerator;
        
        // Store database in .archAngel folder with .ca extension
        var archAngelDir = Path.Combine(workspaceRoot, ".archAngel");
        Directory.CreateDirectory(archAngelDir);
        
        _databasePath = Path.Combine(archAngelDir, "knowledge.ca");
        _connection = new SqliteConnection($"Data Source={_databasePath}");
        _connection.Open();
        
        InitializeDatabase();
        
        _logger.LogInformation("📁 Workspace content store initialized: {DatabasePath}", _databasePath);
    }

    private void InitializeDatabase()
    {
        var createTablesScript = @"
            CREATE TABLE IF NOT EXISTS repositories (
                repository_key TEXT PRIMARY KEY,
                indexed_at DATETIME NOT NULL,
                total_chunks INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS content_chunks (
                id TEXT PRIMARY KEY,
                repository_key TEXT NOT NULL,
                file_path TEXT NOT NULL,
                language TEXT NOT NULL,
                content TEXT NOT NULL,
                chunk_index INTEGER NOT NULL,
                start_line INTEGER NOT NULL,
                end_line INTEGER NOT NULL,
                metadata TEXT,
                embedding BLOB,
                indexed_at DATETIME NOT NULL,
                FOREIGN KEY (repository_key) REFERENCES repositories(repository_key) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_repository_key ON content_chunks(repository_key);
            CREATE INDEX IF NOT EXISTS idx_file_path ON content_chunks(file_path);
            CREATE INDEX IF NOT EXISTS idx_language ON content_chunks(language);
            CREATE INDEX IF NOT EXISTS idx_content_fts ON content_chunks(content);
        ";

        using var command = new SqliteCommand(createTablesScript, _connection);
        command.ExecuteNonQuery();
    }
    public async Task CleanChunksAsync(string repositoryKey)
    {
        using var transaction = _connection.BeginTransaction();

        using (var deleteCommand = new SqliteCommand(
                "DELETE FROM content_chunks WHERE repository_key = @repositoryKey", 
                _connection, transaction))
            {
                deleteCommand.Parameters.AddWithValue("@repositoryKey", repositoryKey);
                var deletedRows = await deleteCommand.ExecuteNonQueryAsync();
                _logger.LogDebug("Deleted {DeletedRows} existing chunks for {Repository}", deletedRows, repositoryKey);
            }
        transaction.Commit();
    }
    
    public async Task StoreChunksAsync(string repositoryKey, List<ContentChunk> chunks)
    {
        try
        {
            _logger.LogInformation("💾 Starting storage of {ChunkCount} chunks for repository {Repository}", chunks.Count, repositoryKey);

            using var transaction = _connection.BeginTransaction();

            // Insert or update repository record with incremental count
            using (var repoCommand = new SqliteCommand(@"
                INSERT INTO repositories (repository_key, indexed_at, total_chunks)
                VALUES (@repositoryKey, @indexedAt, @totalChunks)
                ON CONFLICT(repository_key) DO UPDATE SET
                    indexed_at = @indexedAt,
                    total_chunks = total_chunks + @totalChunks", 
                _connection, transaction))
            {
                repoCommand.Parameters.AddWithValue("@repositoryKey", repositoryKey);
                repoCommand.Parameters.AddWithValue("@indexedAt", DateTime.UtcNow);
                repoCommand.Parameters.AddWithValue("@totalChunks", chunks.Count);
                await repoCommand.ExecuteNonQueryAsync();
                _logger.LogDebug("Updated repository record for {Repository}, added {ChunkCount} chunks", repositoryKey, chunks.Count);
            }

            // Insert chunks in batches for better performance
            const int batchSize = 100;
            int totalProcessed = 0;
            
            for (int i = 0; i < chunks.Count; i += batchSize)
            {
                var batch = chunks.Skip(i).Take(batchSize).ToList();
                int batchStart = i;
                int batchEnd = Math.Min(i + batchSize, chunks.Count);
                
                _logger.LogDebug("Processing batch {BatchStart}-{BatchEnd} of {Total} chunks", 
                    batchStart, batchEnd - 1, chunks.Count);
                using var chunkCommand = new SqliteCommand(@"
                        INSERT INTO content_chunks 
                        (id, repository_key, file_path, language, content, chunk_index, 
                        start_line, end_line, metadata, embedding, indexed_at)
                        VALUES 
                        (@id, @repositoryKey, @filePath, @language, @content, @chunkIndex, 
                        @startLine, @endLine, @metadata, @embedding, @indexedAt)", 
                        _connection, transaction);
                chunkCommand.Parameters.Add(new SqliteParameter("@id", System.Data.DbType.String));
                chunkCommand.Parameters.Add(new SqliteParameter("@repositoryKey", System.Data.DbType.String));
                chunkCommand.Parameters.Add(new SqliteParameter("@filePath", System.Data.DbType.String));
                chunkCommand.Parameters.Add(new SqliteParameter("@language", System.Data.DbType.String));
                chunkCommand.Parameters.Add(new SqliteParameter("@content", System.Data.DbType.String));
                chunkCommand.Parameters.Add(new SqliteParameter("@chunkIndex", System.Data.DbType.Int32));
                chunkCommand.Parameters.Add(new SqliteParameter("@startLine", System.Data.DbType.Int32));
                chunkCommand.Parameters.Add(new SqliteParameter("@endLine", System.Data.DbType.Int32));
                chunkCommand.Parameters.Add(new SqliteParameter("@metadata", System.Data.DbType.String));
                chunkCommand.Parameters.Add(new SqliteParameter("@embedding", System.Data.DbType.Binary));
                chunkCommand.Parameters.Add(new SqliteParameter("@indexedAt", System.Data.DbType.DateTime));
                chunkCommand.Prepare();
                foreach (var chunk in batch)
                {
                    chunkCommand.Parameters["@id"].Value = chunk.Id;
                    chunkCommand.Parameters["@repositoryKey"].Value = repositoryKey;
                    chunkCommand.Parameters["@filePath"].Value = chunk.FilePath;
                    chunkCommand.Parameters["@language"].Value = chunk.Language;
                    chunkCommand.Parameters["@content"].Value = chunk.Content;
                    chunkCommand.Parameters["@chunkIndex"].Value = chunk.ChunkIndex;
                    chunkCommand.Parameters["@startLine"].Value = chunk.StartLine;
                    chunkCommand.Parameters["@endLine"].Value = chunk.EndLine;
                    chunkCommand.Parameters["@metadata"].Value = JsonSerializer.Serialize(chunk.Metadata);

                    
                    // Serialize embedding as byte array
                    
                    if (chunk.Embedding != null && chunk.Embedding.Length > 0)
                    {
                        var embeddingBytes = new byte[chunk.Embedding.Length * sizeof(float)];
                        Buffer.BlockCopy(chunk.Embedding, 0, embeddingBytes, 0, embeddingBytes.Length);
                        chunkCommand.Parameters["@embedding"].Value = embeddingBytes;
                        _logger.LogTrace("Chunk {ChunkId} has embedding of size {Size}", chunk.Id, chunk.Embedding.Length);
                    }
                    else
                    {
                        chunkCommand.Parameters["@embedding"].Value = DBNull.Value;
                        _logger.LogWarning("Chunk {ChunkId} has no embedding", chunk.Id);
                    }

                    chunkCommand.Parameters["@indexedAt"].Value = DateTime.UtcNow;

                    try
                    {
                        await chunkCommand.ExecuteNonQueryAsync();
                        totalProcessed++;
                        
                        if (totalProcessed % 100 == 0)
                        {
                            _logger.LogDebug("Progress: {Processed}/{Total} chunks stored", totalProcessed, chunks.Count);
                        }
                    }
                    catch (Exception chunkEx)
                    {
                        _logger.LogError(chunkEx, "Failed to insert chunk {ChunkId}: {FilePath}", chunk.Id, chunk.FilePath);
                        throw;
                    }
                }
                
                _logger.LogDebug("Batch complete: {BatchSize} chunks processed", batch.Count);
            }

            _logger.LogInformation("Committing transaction for {ChunkCount} chunks", chunks.Count);
            transaction.Commit();
            await GetStatsAsync();
            // Verify the data was stored
            using (var verifyCommand = new SqliteCommand(
                "SELECT COUNT(*) FROM content_chunks WHERE repository_key = @repositoryKey", 
                _connection))
            {
                verifyCommand.Parameters.AddWithValue("@repositoryKey", repositoryKey);
                var storedCount = Convert.ToInt32(await verifyCommand.ExecuteScalarAsync());
                
                
                _logger.LogInformation("✅ Successfully verified storage: {StoredCount} of which {chunk} in database for {Repository}", 
                    storedCount, chunks.Count, repositoryKey);
                
                
            }

            using (var verifyCommand = new SqliteCommand(
                "SELECT COUNT(*) FROM content_chunks", 
                _connection))
            {
                var storedCount = Convert.ToInt32(await verifyCommand.ExecuteScalarAsync());
                
                _logger.LogInformation("✅ Successfully checked total chunks: {StoredCount} chunks in database for {Repository}",storedCount, repositoryKey);
                
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to store chunks for repository {Repository}. Error: {Message}", 
                repositoryKey, ex.Message);
            
            // Log additional SQLite-specific information
            if (ex is SqliteException sqlEx)
            {
                _logger.LogError("SQLite Error Code: {ErrorCode}, Extended Result Code: {ExtendedResultCode}", 
                    sqlEx.SqliteErrorCode, sqlEx.SqliteExtendedErrorCode);
            }
            
            throw;
        }
    }

    /// <summary>
    /// Searches for content chunks matching the query and optional repository filter, returning top results ranked by relevance.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="repositoryFilter">Optional repository filter.</param>
    /// <param name="maxResults">Maximum number of results to return.</param>
    /// <returns>A list of content chunks matching the search criteria.</returns>
    public async Task<List<ContentChunk>> SearchAsync(string query, string? repositoryFilter = null, int maxResults = 10)
    {
        try
        {
            _logger.LogDebug("🔍 Semantic search for: '{Query}'", query);

            var queryEmbedding = await _embeddingGenerator.GenerateAsync(query);

            var res = await HybridSearchAsync(queryEmbedding.Vector.ToArray(), query, repositoryFilter, maxResults);
            if (res.Count == 0)
            {
                return await BackupSearchAsync(query, repositoryFilter, maxResults);
            }
            return res;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Semantic search failed, falling back to keyword search");
            return await BackupSearchAsync(query, repositoryFilter, maxResults);
        }
    }
    
    private async Task<List<ContentChunk>> BackupSearchAsync(string query, string? repositoryFilter = null, int maxResults = 10)
    {
        try
        {
            _logger.LogDebug("🔍 Searching for: '{Query}' in repository: {Repository}", query, repositoryFilter ?? "ALL");

            var searchTerms = ExtractSearchTerms(query);
            var results = new List<(ContentChunk Chunk, double Score)>();

            // Build SQL query with optional repository filter
            var sql = @"
                SELECT id, repository_key, file_path, language, content, 
                       chunk_index, start_line, end_line, metadata
                FROM content_chunks 
                WHERE 1=1";

            var parameters = new List<SqliteParameter>();

            if (!string.IsNullOrEmpty(repositoryFilter))
            {
                sql += " AND repository_key LIKE @repositoryFilter";
                parameters.Add(new SqliteParameter("@repositoryFilter", $"%{repositoryFilter}%"));
            }

            // Add content search
            if (searchTerms.Any())
            {
                var contentConditions = searchTerms.Select((term, index) => {
                    var paramName = $"@term{index}";
                    parameters.Add(new SqliteParameter(paramName, $"%{term}%"));
                    return $"(content LIKE {paramName} OR file_path LIKE {paramName})";
                });
                
                sql += $" AND ({string.Join(" OR ", contentConditions)})";
            }

            sql += " ORDER BY indexed_at DESC";

            using var command = new SqliteCommand(sql, _connection);
            command.Parameters.AddRange(parameters.ToArray());

            using var reader = await command.ExecuteReaderAsync();
            
            while (await reader.ReadAsync())
            {
                var chunk = new ContentChunk(
                    Id: reader.GetString(0), 
                    Content: reader.GetString(4), 
                    FilePath: reader.GetString(2), 
                    Language: reader.GetString(3),
                    ChunkIndex: reader.GetInt32(5), 
                    StartLine: reader.GetInt32(6), 
                    EndLine: reader.GetInt32(7), 
                    Metadata: JsonSerializer.Deserialize<Dictionary<string, object>>(reader.GetString(8)) ?? new() // metadata
                );

                var score = CalculateRelevanceScore(chunk, searchTerms);
                if (score > 0)
                {
                    results.Add((chunk, score));
                }
            }

            // Sort by relevance and return top results
            var topResults = results
                .OrderByDescending(r => r.Score)
                .Take(maxResults)
                .Select(r => r.Chunk)
                .ToList();

            _logger.LogDebug("📊 Found {ResultCount} results for query: '{Query}'", topResults.Count, query);
            return topResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to search for query: '{Query}'", query);
            return new List<ContentChunk>();
        }
    }

    

    public async Task<List<string>> GetIndexedRepositoriesAsync()
    {
        try
        {
            var repositories = new List<string>();
            
            using var command = new SqliteCommand(
                "SELECT repository_key FROM repositories ORDER BY indexed_at DESC", 
                _connection);
            
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                repositories.Add(reader.GetString(0));
            }

            _logger.LogDebug("📋 Retrieved {RepositoryCount} indexed repositories", repositories.Count);
            return repositories;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get indexed repositories");
            return new List<string>();
        }
    }

    public async Task RemoveRepositoryAsync(string repositoryKey)
    {
        try
        {
            using var transaction = _connection.BeginTransaction();

            // Count chunks before deletion for logging
            using var countCommand = new SqliteCommand(
                "SELECT COUNT(*) FROM content_chunks WHERE repository_key = @repositoryKey", 
                _connection, transaction);
            countCommand.Parameters.AddWithValue("@repositoryKey", repositoryKey);
            var chunkCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync());

            // Delete chunks
            using var deleteChunksCommand = new SqliteCommand(
                "DELETE FROM content_chunks WHERE repository_key = @repositoryKey", 
                _connection, transaction);
            deleteChunksCommand.Parameters.AddWithValue("@repositoryKey", repositoryKey);
            await deleteChunksCommand.ExecuteNonQueryAsync();

            // Delete repository record
            using var deleteRepoCommand = new SqliteCommand(
                "DELETE FROM repositories WHERE repository_key = @repositoryKey", 
                _connection, transaction);
            deleteRepoCommand.Parameters.AddWithValue("@repositoryKey", repositoryKey);
            var deletedRepos = await deleteRepoCommand.ExecuteNonQueryAsync();

            transaction.Commit();

            if (deletedRepos > 0)
            {
                _logger.LogInformation("🗑️ Removed repository {Repository} with {ChunkCount} chunks", repositoryKey, chunkCount);
            }
            else
            {
                _logger.LogWarning("⚠️ Repository {Repository} not found for removal", repositoryKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to remove repository {Repository}", repositoryKey);
            throw;
        }
    }

    public async Task<ContentChunk?> GetChunkAsync(string chunkId)
    {
        try
        {
            using var command = new SqliteCommand(@"
                SELECT id, repository_key, file_path, language, content, 
                       chunk_index, start_line, end_line, metadata
                FROM content_chunks 
                WHERE id = @chunkId", _connection);
            
            command.Parameters.AddWithValue("@chunkId", chunkId);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new ContentChunk(
                    Id: reader.GetString(0), // id
                    Content: reader.GetString(4), // content
                    FilePath: reader.GetString(2), // file_path
                    Language: reader.GetString(3), // language
                    ChunkIndex: reader.GetInt32(5), // chunk_index
                    StartLine: reader.GetInt32(6), // start_line
                    EndLine: reader.GetInt32(7), // end_line
                    Metadata: JsonSerializer.Deserialize<Dictionary<string, object>>(reader.GetString(8)) ?? new() // metadata
                );
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get chunk {ChunkId}", chunkId);
            return null;
        }
    }

    public async Task<ContentStoreStats> GetStatsAsync()
    {
        try
        {
            using var statsCommand = new SqliteCommand(@"
                SELECT 
                    COUNT(DISTINCT repository_key) as repo_count,
                    COUNT(*) as chunk_count,
                    SUM(LENGTH(content)) as total_size
                FROM content_chunks", _connection);

            using var reader = await statsCommand.ExecuteReaderAsync();
            await reader.ReadAsync();

            var totalRepositories = reader.GetInt32(0);
            var totalChunks = reader.GetInt32(1);
            var totalContentSize = reader.GetInt64(2);

            reader.Close();

            // Get language distribution
            using var langCommand = new SqliteCommand(@"
                SELECT language, COUNT(*) as count 
                FROM content_chunks 
                GROUP BY language", _connection);

            var languageDistribution = new Dictionary<string, int>();
            using var langReader = await langCommand.ExecuteReaderAsync();
            while (await langReader.ReadAsync())
            {
                languageDistribution[langReader.GetString(0)] = langReader.GetInt32(1);
            }
            using var filesCommand = new SqliteCommand(@"
                SELECT id, file_path
                FROM content_chunks", _connection);
            List<string> filesList = new List<string>();
            using var fReader = await filesCommand.ExecuteReaderAsync();
            
            while (await fReader.ReadAsync())
            {
                Console.Error.WriteLine($"[DEBUG] File_path: {fReader.GetString(1)}");
                filesList.Add(fReader.GetString(1));
            }


            return new ContentStoreStats(
                TotalRepositories: totalRepositories,
                TotalChunks: totalChunks,
                TotalContentSize: totalContentSize,
                LanguageDistribution: languageDistribution,
                LastUpdated: DateTime.UtcNow,
                files: filesList
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get content store stats");
            return new ContentStoreStats(0, 0, 0, new Dictionary<string, int>(), DateTime.UtcNow , []);
        }
    }

    public string GetDatabasePath() => _databasePath;
    public string GetWorkspaceRoot() => _workspaceRoot;

    public long GetDatabaseSizeAsync()
    {
        try
        {
            var fileInfo = new FileInfo(_databasePath);
            return fileInfo.Exists ? fileInfo.Length : 0;
        }
        catch
        {
            return 0;
        }
    }

    private List<string> ExtractSearchTerms(string query)
    {
        return query
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(term => term.Length > 2)
            .Distinct()
            .ToList();
    }

    private double CalculateRelevanceScore(ContentChunk chunk, List<string> searchTerms)
    {
        if (!searchTerms.Any()) return 0;

        var content = chunk.Content.ToLowerInvariant();
        var filePath = chunk.FilePath.ToLowerInvariant();
        double score = 0;

        foreach (var term in searchTerms)
        {
            // Count occurrences in content
            var contentMatches = CountOccurrences(content, term);
            score += contentMatches * 1.0;

            // Boost score for matches in file path/name
            var pathMatches = CountOccurrences(filePath, term);
            score += pathMatches * 2.0;

            // Boost score for exact word matches
            var wordBoundaryPattern = $@"\b{Regex.Escape(term)}\b";
            var exactMatches = Regex.Matches(content, wordBoundaryPattern).Count;
            score += exactMatches * 0.5;
        }

        // Apply multipliers based on content characteristics
        if (chunk.Language == "markdown" || chunk.Language == "text")
        {
            score *= 0.8; // Slightly lower priority for documentation
        }

        if (chunk.FilePath.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            score *= 1.2; // Boost test files as they often contain usage examples
        }

        // Normalize by content length to avoid bias toward longer chunks
        var lengthNormalization = Math.Min(1.0, 1000.0 / chunk.Content.Length);
        score *= lengthNormalization;

        return score;
    }

    public async Task WipeDatabaseAsync()
    {
        try
        {
            using var transaction = _connection.BeginTransaction();
            
            // Count existing data for logging
            using var countChunksCommand = new SqliteCommand("SELECT COUNT(*) FROM content_chunks", _connection, transaction);
            var chunkCount = Convert.ToInt32(await countChunksCommand.ExecuteScalarAsync());
            
            using var countReposCommand = new SqliteCommand("SELECT COUNT(*) FROM repositories", _connection, transaction);
            var repoCount = Convert.ToInt32(await countReposCommand.ExecuteScalarAsync());
            
            _logger.LogInformation("🗑️ Wiping database: {RepoCount} repositories, {ChunkCount} chunks", repoCount, chunkCount);
            
            // Delete all chunks
            using var deleteChunksCommand = new SqliteCommand("DELETE FROM content_chunks", _connection, transaction);
            await deleteChunksCommand.ExecuteNonQueryAsync();
            
            // Delete all repositories
            using var deleteReposCommand = new SqliteCommand("DELETE FROM repositories", _connection, transaction);
            await deleteReposCommand.ExecuteNonQueryAsync();
            
            // Reset SQLite auto-increment counters and optimize
            transaction.Commit();

            using var vacuumCommand = new SqliteCommand("VACUUM", _connection);
            await vacuumCommand.ExecuteNonQueryAsync();
            _logger.LogInformation("✅ Database wiped successfully: removed {RepoCount} repositories and {ChunkCount} chunks", repoCount, chunkCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to wipe database");
            throw;
        }
    }

    private int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int index = 0;

        while ((index = text.IndexOf(pattern, index, StringComparison.OrdinalIgnoreCase)) != -1)
        {
            count++;
            index += pattern.Length;
        }

        return count;
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
    public async Task<List<ContentChunk>> VectorSearchAsync(float[] queryEmbedding, string? repositoryFilter = null, int maxResults = 50)
    {
        try
        {
            _logger.LogDebug("🔍 Vector search in repository: {Repository}", repositoryFilter ?? "ALL");

            // Diagnostic: check total chunks and how many have embeddings
            using (var diagCmd = new SqliteCommand(
                "SELECT COUNT(*) as total, SUM(CASE WHEN embedding IS NOT NULL THEN 1 ELSE 0 END) as with_emb, SUM(CASE WHEN embedding IS NOT NULL THEN LENGTH(embedding) ELSE 0 END) as total_emb_bytes FROM content_chunks", _connection))
            {
                using var diagReader = await diagCmd.ExecuteReaderAsync();
                if (await diagReader.ReadAsync())
                {
                    _logger.LogInformation("📊 DB diagnostics: total chunks={Total}, with embeddings={WithEmb}, total embedding bytes={TotalBytes}",
                        diagReader.GetInt32(0), diagReader.GetInt32(1), diagReader.GetInt64(2));
                }
            }

            _logger.LogDebug("🔍 Query embedding length: {Length}, sample values: [{V0:F4}, {V1:F4}, {V2:F4}]",
                queryEmbedding.Length,
                queryEmbedding.Length > 0 ? queryEmbedding[0] : 0f,
                queryEmbedding.Length > 1 ? queryEmbedding[1] : 0f,
                queryEmbedding.Length > 2 ? queryEmbedding[2] : 0f);

            var sql = @"
                SELECT id, repository_key, file_path, language, content, 
                       chunk_index, start_line, end_line, metadata, embedding
                FROM content_chunks 
                WHERE embedding IS NOT NULL";

            var parameters = new List<SqliteParameter>();

            if (!string.IsNullOrEmpty(repositoryFilter))
            {
                sql += " AND repository_key LIKE @repositoryFilter";
                parameters.Add(new SqliteParameter("@repositoryFilter", $"%{repositoryFilter}%"));
            }

            using var command = new SqliteCommand(sql, _connection);
            command.Parameters.AddRange(parameters.ToArray());

            var results = new List<(ContentChunk Chunk, float Score)>();
            int rowsRead = 0;
            int embeddingsDeserialized = 0;

            using var reader = await command.ExecuteReaderAsync();
            
            while (await reader.ReadAsync())
            {
                rowsRead++;
                // Deserialize embedding
                var embeddingBytes = reader.IsDBNull(9) ? null : (byte[])reader["embedding"];
                float[]? embedding = null;
                
                if (embeddingBytes != null && embeddingBytes.Length > 0)
                {
                    embedding = new float[embeddingBytes.Length / sizeof(float)];
                    Buffer.BlockCopy(embeddingBytes, 0, embedding, 0, embeddingBytes.Length);
                    embeddingsDeserialized++;
                }

                if (embedding == null)
                    continue;

                var chunk = new ContentChunk(
                    Id: reader.GetString(0),
                    Content: reader.GetString(4),
                    FilePath: reader.GetString(2),
                    Language: reader.GetString(3),
                    ChunkIndex: reader.GetInt32(5),
                    StartLine: reader.GetInt32(6),
                    EndLine: reader.GetInt32(7),
                    Metadata: JsonSerializer.Deserialize<Dictionary<string, object>>(reader.GetString(8)) ?? new(),
                    Embedding: embedding
                );

                // Calculate cosine similarity
                var similarity = TensorPrimitives.CosineSimilarity(queryEmbedding, embedding);
                
                if (rowsRead <= 3)
                {
                    _logger.LogDebug("🔍 Sample row {Row}: id={Id}, embedding length={EmbLen}, similarity={Sim:F4}",
                        rowsRead, chunk.Id, embedding.Length, similarity);
                }
                
                results.Add((chunk, similarity));
            }

            _logger.LogInformation("📊 Vector search: {RowsRead} rows read, {EmbDeserialized} embeddings deserialized, {ResultCount} scored",
                rowsRead, embeddingsDeserialized, results.Count);

            // Return top results by similarity
            var topResults = results
                .Where(r => r.Score > 0.3)
                .OrderByDescending(r => r.Score)
                .Take(maxResults)
                .Select(r => r.Chunk)
                .ToList();

            _logger.LogDebug("📊 Vector search found {ResultCount} results (threshold: 0.3, max score: {MaxScore:F3})", 
                topResults.Count, results.Count > 0 ? results.Max(r => r.Score) : 0f);
            return topResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Vector search failed");
            return new List<ContentChunk>();
        }
    }

    public async Task<List<ContentChunk>> HybridSearchAsync(float[] queryEmbedding, string query, string? repositoryFilter = null, int maxResults = 10)
    {
        try
        {
            _logger.LogDebug("🔍 Hybrid search for: '{Query}' in repository: {Repository}", query, repositoryFilter ?? "ALL");

            // Get vector results (semantic search)
            var vectorResults = await VectorSearchAsync(queryEmbedding, repositoryFilter, 50);
            
            // Get keyword results (traditional search)
            var keywordResults = await BackupSearchAsync(query, repositoryFilter, 20);

            // Merge and rank results
            var allChunks = vectorResults.Union(keywordResults, new ContentChunkIdComparer()).ToList();
            
            // Normalize scores using rank-based scoring
            var vectorScores = vectorResults
                .Select((chunk, index) => (chunk.Id, Score: 1.0 - (double)index / vectorResults.Count))
                .ToDictionary(x => x.Id, x => x.Score);

            var keywordScores = keywordResults
                .Select((chunk, index) => (chunk.Id, Score: 1.0 - (double)index / keywordResults.Count))
                .ToDictionary(x => x.Id, x => x.Score);

            // Combine scores: 70% vector + 30% keyword
            var hybridScores = allChunks.Select(chunk => {
                var vectorScore = vectorScores.GetValueOrDefault(chunk.Id, 0.0);
                var keywordScore = keywordScores.GetValueOrDefault(chunk.Id, 0.0);
                var combinedScore = (0.7 * vectorScore) + (0.3 * keywordScore);
                return (chunk, combinedScore);
            });

            // Return top results
            var topResults = hybridScores
                .OrderByDescending(r => r.combinedScore)
                .Take(maxResults)
                .Select(r => r.chunk)
                .ToList();

            _logger.LogDebug("📊 Hybrid search found {ResultCount} results (from {VectorCount} vector + {KeywordCount} keyword)", 
                topResults.Count, vectorResults.Count, keywordResults.Count);

            return topResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Hybrid search failed");
            // Fallback to keyword search
            return await BackupSearchAsync(query, repositoryFilter, maxResults);
        }
    }

    // Helper class for comparing chunks by ID
    private class ContentChunkIdComparer : IEqualityComparer<ContentChunk>
    {
        public bool Equals(ContentChunk? x, ContentChunk? y)
        {
            if (x == null || y == null) return false;
            return x.Id == y.Id;
        }

        public int GetHashCode(ContentChunk obj) => obj.Id.GetHashCode();
    }
}