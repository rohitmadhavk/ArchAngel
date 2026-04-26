using System.Text.Json;

namespace ArchAngel.Service.Configuration;

/// <summary>
/// Simple configuration for archAngel.json
/// </summary>
public class ArchAngelConfig
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public List<string> Repositories { get; set; } = new();
}

/// <summary>
/// Manages hierarchical archAngel.json configuration resolution
/// </summary>
public class ConfigurationManager
{
    private readonly ILogger<ConfigurationManager> _logger;

    public ConfigurationManager(ILogger<ConfigurationManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Resolves configuration files hierarchically (like Node.js module resolution)
    /// </summary>
    public async Task<ArchAngelConfig> ResolveConfigurationAsync(string workspaceRoot)
    {
        var mergedConfig = new ArchAngelConfig();
        var configs = await GetHierarchicalConfigsAsync(workspaceRoot);

        // Merge configs from root to leaf (child configs override parent)
        foreach (var config in configs)
        {
            if (!string.IsNullOrEmpty(config.Name))
                mergedConfig.Name = config.Name;
            
            if (!string.IsNullOrEmpty(config.Version))
                mergedConfig.Version = config.Version;
            
            // Merge repositories (child configs extend parent configs)
            foreach (var repo in config.Repositories)
            {
                if (!mergedConfig.Repositories.Contains(repo))
                {
                    mergedConfig.Repositories.Add(repo);
                }
            }
        }

        _logger.LogInformation("📋 Resolved {RepoCount} repositories from {ConfigCount} config files", 
            mergedConfig.Repositories.Count, configs.Count);

        return mergedConfig;
    }

    /// <summary>
    /// Walks up directory tree to find all archAngel.json files
    /// </summary>
    private async Task<List<ArchAngelConfig>> GetHierarchicalConfigsAsync(string startPath)
    {
        var configs = new List<ArchAngelConfig>();
        var currentPath = Path.GetFullPath(startPath);

        while (!string.IsNullOrEmpty(currentPath))
        {
            var configPath = Path.Combine(currentPath, "archAngel.json");
            
            if (File.Exists(configPath))
            {
                try
                {
                    var configContent = await File.ReadAllTextAsync(configPath);
                    var config = JsonSerializer.Deserialize<ArchAngelConfig>(configContent) ?? new ArchAngelConfig();
                    configs.Insert(0, config); // Insert at beginning for parent-first order
                    _logger.LogDebug("📄 Found config at: {ConfigPath}", configPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Failed to parse config at: {ConfigPath}", configPath);
                }
            }

            // Move up one directory
            var parent = Directory.GetParent(currentPath);
            currentPath = parent?.FullName;
            
            // Stop at drive root
            if (parent == null || currentPath == parent.FullName)
                break;
        }

        return configs;
    }

    /// <summary>
    /// Saves configuration to workspace root
    /// </summary>
    public async Task SaveConfigurationAsync(string workspaceRoot, ArchAngelConfig config)
    {
        var configPath = Path.Combine(workspaceRoot, "archAngel.json");
        var options = new JsonSerializerOptions { WriteIndented = true };
        var configContent = JsonSerializer.Serialize(config, options);
        
        await File.WriteAllTextAsync(configPath, configContent);
        _logger.LogInformation("💾 Saved config to: {ConfigPath}", configPath);
    }
}