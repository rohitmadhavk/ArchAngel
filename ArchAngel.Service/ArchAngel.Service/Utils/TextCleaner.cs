using System.Text;
using ArchAngel.Service.Utilities;

class TextCleaner : ITextCleaner
{
    private readonly ILogger<TextCleaner> _logger;
    public TextCleaner(ILogger<TextCleaner> logger)
    {
        _logger = logger;
    }
    public string CleanTextForTokenizer(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        try
        {
            _logger.LogDebug($"[DEBUG] Cleaning text of length: {text.Length}");
            
            // Remove or replace problematic characters that can cause tokenization issues
            var cleaned = text
                .Replace('\0', ' ')          // Replace null characters
                .Replace('\r', '\n')         // Normalize line endings
                .Replace('\u00A0', ' ')      // Replace non-breaking spaces
                .Replace('\uFEFF', ' ')      // Replace BOM character
                .Replace('\b', ' ')          // Replace backspace
                .Replace('\f', ' ')          // Replace form feed
                .Replace('\v', ' ')          // Replace vertical tab
                .Replace('\x7F', ' ');       // Replace DEL character

            // Remove any control characters except tab and newline
            var sb = new StringBuilder(cleaned.Length);
            foreach (char c in cleaned)
            {
                if (char.IsControl(c) && c != '\t' && c != '\n')
                {
                    sb.Append(' '); // Replace control chars with space
                }
                else if (c == '\t')
                {
                    sb.Append("    "); // Replace tabs with 4 spaces
                }
                else
                {
                    sb.Append(c);
                }
            }

            // Collapse multiple consecutive spaces/newlines
            var result = sb.ToString();
            while (result.Contains("  "))
            {
                result = result.Replace("  ", " ");
            }
            while (result.Contains("\n\n\n"))
            {
                result = result.Replace("\n\n\n", "\n\n");
            }

            var finalResult = result.Trim();
            _logger.LogDebug($"[DEBUG] Cleaned text length: {finalResult.Length}");
            return finalResult;
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[WARNING] Error cleaning text: {ex.Message}");
            // Return a safe fallback
            return "[Content cleaning failed - file may contain binary data]";
        }
    }
}