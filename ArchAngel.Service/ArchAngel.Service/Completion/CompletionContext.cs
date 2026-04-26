namespace ArchAngel.Service.Completion;
public record CompletionContextData
    (
        string context,
        string beforeCursor,
        string language,
        int cursorLine
    );