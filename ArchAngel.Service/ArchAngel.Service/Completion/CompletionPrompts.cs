namespace ArchAngel.Service.Completion;
public static class CompletionPrompts
{

    public static string BuildStandardPrompt(CompletionContextData completionContext)
    {
        var context = completionContext.context;
        var beforeCursor = completionContext.beforeCursor;
        var language = completionContext.language;
        var cursorLine = completionContext.cursorLine;
        var prompt = $@"
    You are an expert code completion assistant. Based on the following code context, suggest relevant completions for the cursor position.

    Language: {language}
    Context:
    ```{language.ToLower()}
    {context}
    ```

    The cursor is at line {cursorLine}, after: ""{beforeCursor}""

    Provide 3-5 specific, descriptive and relevant code completions. Label with the code to insert. Format as JSON array:
    [
    {{""label"": ""code_to_insert"", ""kind"": ""function|variable|keyword|class|method"", ""detail"": ""description"", ""insertText"": ""code_to_insert""}}
    ]

    Focus on:
    - Language-specific syntax and patterns  
    - Variable/function names that make sense in context
    - Common programming patterns for this language
    - Only suggest what would logically come next

    Respond with ONLY the JSON array, no explanations.";
    return prompt;
    }
    public static string BuildGoldenRepoCompletionPrompt(CompletionContextData completionContext, string goldenContext)
    {
        var context = completionContext.context;
        var beforeCursor = completionContext.beforeCursor;
        var language = completionContext.language;
        var cursorLine = completionContext.cursorLine;
        var prompt = $@"
You are a Senior Software Architect AI Agent providing intelligent code completions based on golden repository patterns.

COMPLETION CONTEXT:
Language: {language}
Current Code Context:
```{language.ToLower()}
{context}
```
Cursor Position: Line {cursorLine}, after: ""{beforeCursor}""

GOLDEN REPOSITORY PATTERNS:
{goldenContext}

AGENT INSTRUCTIONS:
As an expert architect, analyze the current context and golden patterns to suggest completions or rewrites of the line that:
1. Follow established patterns from the golden repositories
2. Maintain architectural consistency
3. Apply best practices from proven implementations
4. If a completion is inspired by a golden repository pattern, include the corresponding [CITE:X] number as ""citationId"" in the JSON. Otherwise set ""citationId"" to null.


Provide at least one intelligent completion per source and at least 3-5 overall, (1-3 lines for each completion, just the next logical statement). When inspired by golden patterns, reference the [CITE:X] numbers.
Format as JSON array:
[
{{""label"": ""code_to_insert"", ""kind"": ""function|variable|keyword|class|method"", ""detail"": ""architectural_description"", ""insertText"": ""code_to_insert"", ""citationId"": null_or_number}}
]

Focus on architectural soundness and pattern consistency. Include citationId if inspired by golden repository patterns.
Respond with ONLY the JSON array.";
    Console.Error.WriteLine(prompt);
        return prompt;
    }

}