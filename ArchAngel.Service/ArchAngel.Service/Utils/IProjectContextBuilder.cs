namespace ArchAngel.Service.Utilities;

public interface IProjectContextBuilder
{
    string GetProjectContext(string? filePath, Dictionary<string, string> sessionDocuments);

}