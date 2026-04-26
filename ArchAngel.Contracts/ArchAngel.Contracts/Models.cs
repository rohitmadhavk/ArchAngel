namespace ArchAngel.Contracts;

public record CodeSuggestion(
    string type,
    string message, 
    int line,
    string explanation,
    string? example = null,
    string? bestPracticeId = null
);



public record GeneratedDocResponse(
    bool success,
    string docPath
);

public record ContentSource(
    string id,
    string filePath,
    string repository,
    string language,
    int chunkIndex,
    int startLine,
    int endLine,
    int? citationId = null,
    double? relevanceScore = null,
    string? githubUrl = null
);

public record ArchAngelChatResponse(
    string message,
    string sessionId,
    List<CodeSuggestion>? suggestions = null,
    List<ContentSource>? sources = null,
    string? ragContext = null
);

public record PingMultipleResponse(
    string response,
    string message,
    int number,
    string? optional,
    DateTime timestamp
);

public record IndexRepositoryResponse(
    bool success,
    string message,
    int? indexedFiles = null
);