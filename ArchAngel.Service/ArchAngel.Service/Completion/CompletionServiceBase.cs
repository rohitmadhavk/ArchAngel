using System.Text.Json;
using ArchAngel.Contracts;
using ArchAngel.Service.Chat;
using ArchAngel.Service.Completion;
using ArchAngel.Service.Storage;
using ArchAngel.Service.Utilities;
using Microsoft.VisualStudio.LanguageServer.Protocol;

public abstract class CompletionServiceBase : ICompletionService
{
    private readonly GoldenRepoSearchService _goldenRepoSearchService;
    private readonly IContentStoreProvider _contentStoreProvider;
    private CancellationTokenSource _completionCts;

    private readonly ILogger<CompletionServiceBase> _logger;

    protected CompletionServiceBase(
        GoldenRepoSearchService goldenRepoSearchService,
        IContentStoreProvider contentStoreProvider,
        ILogger<CompletionServiceBase> logger
    )
    {
        _goldenRepoSearchService = goldenRepoSearchService;
        _contentStoreProvider = contentStoreProvider;
        _logger = logger;
    }
    public async Task<CompletionItem[]?> GetCompletionsAsync(string uri,
        int line,
        int character,
        string content)
    {
        _completionCts?.Cancel();
        _completionCts = new CancellationTokenSource();
        var token = _completionCts.Token;
        try
        {
            await Task.Delay(300,token);
            var completionContext = await GetCompletionContext(uri, line, character, content);
            token.ThrowIfCancellationRequested();
            var statsTask = _contentStoreProvider.GetContentStore().GetDatabaseSizeAsync();
            var goldenRepoExists = statsTask > 0;
            
            var completionItems = await ProcessCompletionsAsync(completionContext, token, goldenRepoExists);
            return completionItems;
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("Cancelling Completion: waiting for next ask");
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Message);
            return [];
        }
    }

    public Task<CompletionItem> ResolveCompletionAsync(
        CompletionItem item)
    {
        try
        {
            Console.Error.WriteLine($"[DEBUG] ResolveCompletionAsync called with parameters:");
            Console.Error.WriteLine($"[DEBUG] - label: {item.Label}");
            Console.Error.WriteLine($"[DEBUG] - kind: {item.Kind}");
            Console.Error.WriteLine($"[DEBUG] - detail: {item.Detail}");
            Console.Error.WriteLine($"[DEBUG] - documentation: {item}");
            Console.Error.WriteLine($"[DEBUG] - insertText: {item.InsertText}");
            Console.Error.WriteLine($"[DEBUG] - data: {item.Data}");
            
            // If the completion has source data, enhance the documentation
            if (item.Data != null && !string.IsNullOrEmpty(item.Data.ToString()))
            {
                try
                {
                    var dataString = item.Data.ToString()!;
                    var sourceData = JsonSerializer.Deserialize<ContentSource>(dataString);
                    if (sourceData != null)
                    {
                        // Create rich documentation with source reference
                        var enhancedDoc = new MarkupContent
                        {
                            Kind = MarkupKind.Markdown,
                            Value = $@"## {item.Label}

    **{item.Detail}**

    ### 📚 Source Reference
    - **File:** [{sourceData.filePath}]({sourceData.githubUrl})
    - **Repository:** {sourceData.repository}
    - **Language:** {sourceData.language}
    - **Lines:** {sourceData.startLine}-{sourceData.endLine}

    *This completion is inspired by patterns found in the indexed codebase.*

    ---
    💡 **Tip:** Click the file link above to view the source code in GitHub."
                        };
                        
                        item.Documentation = enhancedDoc;
                        Console.Error.WriteLine($"[DEBUG] Enhanced documentation for {item.Label} with source from {sourceData.filePath}");
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[DEBUG] Failed to parse source data for completion: {ex.Message}");
                }
            }
            else if (item.Documentation?.Value is string docString && !string.IsNullOrEmpty(docString))
            {
                // Convert existing documentation to markdown
                item.Documentation = new MarkupContent
                {
                    Kind = MarkupKind.Markdown,
                    Value = $"## {item.Label}\n\n{docString}"
                };
            }
            
            Console.Error.WriteLine($"[DEBUG] Resolved completion item: {item.Label}");
            return Task.FromResult(item);
        } catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] ResolveCompletionAsync failed: {ex.Message}");
            _logger.LogError(ex, "Error in ResolveCompletionAsync");
            
            // Return a basic completion item as fallback
            return Task.FromResult(new CompletionItem
            {
                Label = item.Label.ToString() ?? "Error",
                Kind = CompletionItemKind.Text,
                Detail = "Error resolving completion"
            });
        }
    }

    private async Task<CompletionContextData> GetCompletionContext(string uri, int line, int character, string content)
    {
            Console.Error.WriteLine($"[DEBUG] Final extracted values - URI: '{uri ?? "NULL"}', Line: {line}, Character: {character}");
            
            if (string.IsNullOrEmpty(uri))
            {
                Console.Error.WriteLine("[DEBUG] URI is still null or empty!");
                throw new Exception("URI empty");
            }
            

            // Get context around the cursor
            var lines = content.Split('\n');
            if (line >= lines.Length) throw new Exception("No content found in file");

            var currentLine = lines[line];
            var beforeCursor = currentLine.Substring(0, Math.Min(character, currentLine.Length));
            
            // Get surrounding context (more lines for better AI context)
            var contextStart = Math.Max(0, line - 10);
            var contextEnd = Math.Min(lines.Length, line + 5);
            var contextLines = lines[contextStart..contextEnd];
            var contextText = string.Join("\n", contextLines);
            
            // Get language for better suggestions
            var language = LanguageHelper.GetLanguageFromUri(uri);
            CompletionContextData completionContext = new CompletionContextData(contextText, beforeCursor, language, line - contextStart);
            
            Console.Error.WriteLine($"[DEBUG] Language: {language}, Before cursor: '{beforeCursor}'");
            return completionContext;
    }
    
    private async Task<CompletionItem[]?> ProcessCompletionsAsync(CompletionContextData completionContext, CancellationToken cancellationToken, bool goldenRepo)
    {
        try
        {
            var context = completionContext.context;
            var beforeCursor = completionContext.beforeCursor;
            var language = completionContext.language;
            var cursorLine = completionContext.cursorLine;
        
            // First check if we have golden repositories indexed
            
            if (goldenRepo)
            {
                Console.Error.WriteLine("[DEBUG] Golden repositories available - delegating to golden repo completions");
                return await GetGoldenRepoCompletionsAsync(completionContext, cancellationToken);
            }
            
            Console.Error.WriteLine("[DEBUG] No golden repositories - using standard AI completions");
            
            return await GetStandardCompletionsAsync(completionContext,cancellationToken);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DEBUG] AI completion failed: {ex.Message}");
            return null;
        }
        
    }

    

    protected abstract Task<string> InvokePromptAsync(string prompt, CancellationToken cancellationToken);
    private async Task<CompletionItem[]?> GetStandardCompletionsAsync(CompletionContextData completionContext, CancellationToken cancellationToken)
    {
        // Standard AI completion prompt without golden repo context
        var prompt = CompletionPrompts.BuildStandardPrompt(completionContext);
        
        var responseText = await InvokePromptAsync(prompt, cancellationToken);
        
        Console.Error.WriteLine($"[DEBUG] Standard AI completion response: {responseText}");
        
        // Parse standard completions (same logic as before)
        if (responseText.StartsWith('[') && responseText.EndsWith(']'))
        {
            var suggestions = JsonSerializer.Deserialize<JsonElement[]>(responseText);
            var completionItems = new List<CompletionItem>();
            if(suggestions == null)
            {
                return [];
            }
            foreach (var suggestion in suggestions)
            {
                if (suggestion.TryGetProperty("label", out var label) &&
                    suggestion.TryGetProperty("kind", out var kind) &&
                    suggestion.TryGetProperty("detail", out var detail))
                {
                    var insertText = suggestion.TryGetProperty("insertText", out var insert) 
                        ? insert.GetString() 
                        : label.GetString();

                    var completionKind = MapStringToCompletionItemKind(kind.GetString());
                    
                    if (!string.IsNullOrEmpty(insertText))
                    {
                        insertText = insertText.Replace("\n", "").Replace("\t", "").Replace("\r", "");
                    }
                    
                    completionItems.Add(new CompletionItem
                    {
                        Label = label.GetString() ?? "",
                        Kind = completionKind,
                        Detail = detail.GetString(),
                        InsertText = insertText,
                        Documentation = $"🏗️ Architect-suggested `{kind.GetString()}`"
                    });
                }
            }
            
            return completionItems.ToArray();
        } else
        {
            return null;
        }
    }
    private async Task<CompletionItem[]?> GetGoldenRepoCompletionsAsync(CompletionContextData completionContext, CancellationToken cancellationToken)
    {
        try
        {
            
            var beforeCursor = completionContext.beforeCursor;
            var language = completionContext.language;
            Console.Error.WriteLine($"[DEBUG] Getting golden repository completions for {language}");
            // Search for relevant golden repository patterns
            var searchQuery = $"{language} {completionContext.context}";
            List<ContentSource> sources;
            string goldenContext;

            (goldenContext, sources) = await _goldenRepoSearchService.SearchGoldenRepos(searchQuery, 8);
            
            var prompt = CompletionPrompts.BuildGoldenRepoCompletionPrompt(completionContext, goldenContext);

            Console.Error.WriteLine($"[DEBUG] Found {sources.Count} golden repository patterns for completions");
            
            var response = await InvokePromptAsync(prompt, cancellationToken);
            var responseText = response.ToString().Trim().Replace("```json", "").Replace("```", "").Trim();
            
            Console.Error.WriteLine($"[DEBUG] Golden repo agent completion response: {responseText}");
            
            // Parse agent completions with citation support
            if (responseText.StartsWith('[') && responseText.EndsWith(']'))
            {
                var suggestions = JsonSerializer.Deserialize<JsonElement[]>(responseText);
                var completionItems = new List<CompletionItem>();
                if(suggestions == null)
                {
                    return [];
                }
                foreach (var suggestion in suggestions)
                {
                    if (suggestion.TryGetProperty("label", out var label) &&
                        suggestion.TryGetProperty("kind", out var kind) &&
                        suggestion.TryGetProperty("detail", out var detail))
                    {
                        var insertText = suggestion.TryGetProperty("insertText", out var insert) 
                            ? insert.GetString() 
                            : label.GetString();

                        var completionKind = MapStringToCompletionItemKind(kind.GetString());
                        

                        // Check if this completion has a citation
                        var citationId = suggestion.TryGetProperty("citationId", out var citeElement) 
                            ? citeElement.ValueKind switch
                            {
                                JsonValueKind.Number => citeElement.GetInt32(),
                                JsonValueKind.String when int.TryParse(citeElement.GetString(), out var parsed) => parsed,
                                _ => (int?)null
                            }
                            : (int?)null;

                        // Build documentation with citation if available
                        MarkupContent documentation;
                        
                        if (citationId.HasValue && sources.Any(s => s.citationId == citationId.Value))
                        {
                            var source = sources.First(s => s.citationId == citationId.Value);
                            var commentPrefix = LanguageHelper.GetCommentPrefix(language); // e.g. "// " for C#
                            var citation = $"{commentPrefix}Source: {source.filePath} ({source.repository}) L{source.startLine}-L{source.endLine} | {source.githubUrl}";
                            insertText = citation + "\n" + insertText;
                            var documentationContent = $@"## 🏗️ {label.GetString()}
**{detail.GetString()}**

### 📚 **Golden Repository Pattern**
- **Source:** [{source.filePath}]({source.githubUrl})  
- **Repository:** {source.repository}
- **Language:** {source.language}
- **Lines:** {source.startLine}-{source.endLine}

*This completion follows architectural patterns from proven golden repositories.*

---
💡 **Architectural Insight:** This pattern has been validated in production environments.";
                            documentation = new MarkupContent
                            {
                                Kind = MarkupKind.Markdown,
                                Value = documentationContent
                            };
                        } else {
                            documentation = new MarkupContent
                            {
                                Kind = MarkupKind.Markdown,
                                Value = $"🏗️ Architect-suggested `{kind.GetString()}`"
                            };
                        }

                        completionItems.Add(new CompletionItem
                        {
                            Label = label.GetString() ?? "",
                            Kind = completionKind,
                            Detail = $"🏗️ {detail.GetString()}", // Architect badge
                            InsertText = insertText,
                            Documentation = documentation
                        });
                    }
                }
                
                Console.Error.WriteLine($"[DEBUG] Generated {completionItems.Count} golden repo completions");
                return completionItems.ToArray();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DEBUG] Golden repo completion failed: {ex.Message}");
        }
        
        return null;
    }
    private CompletionItemKind MapStringToCompletionItemKind(string? kind)
    {
        return kind?.ToLowerInvariant() switch
        {
            "function" or "method" => CompletionItemKind.Function,
            "variable" => CompletionItemKind.Variable,
            "keyword" => CompletionItemKind.Keyword,
            "class" => CompletionItemKind.Class,
            "interface" => CompletionItemKind.Interface,
            "module" => CompletionItemKind.Module,
            "property" => CompletionItemKind.Property,
            "unit" => CompletionItemKind.Unit,
            "value" => CompletionItemKind.Value,
            "enum" => CompletionItemKind.Enum,
            "snippet" => CompletionItemKind.Snippet,
            "text" => CompletionItemKind.Text,
            _ => CompletionItemKind.Text
        };
    }
}