using System.Text.Json;

namespace AppCap;

public static class ConfigLoader
{
    public const string FileName = "appcap.config.json";

    public static TargetCatalog Load(string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(baseDirectory);
        return LoadFromFile(Path.Combine(baseDirectory, FileName));
    }

    public static TargetCatalog LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new AppCapException(
                $"Configuration file '{path}' was not found. Create it next to appcap.exe. See the README for the expected format.",
                ExitCodes.UsageError);
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (IOException exception)
        {
            throw new AppCapException($"Configuration file '{path}' could not be read: {exception.Message}", exception, ExitCodes.UsageError);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new AppCapException($"Configuration file '{path}' could not be read: {exception.Message}", exception, ExitCodes.UsageError);
        }

        AppCapConfig? config;
        try
        {
            config = JsonSerializer.Deserialize(json, AppCapConfigJsonContext.Default.AppCapConfig);
        }
        catch (JsonException exception)
        {
            throw new AppCapException($"Configuration file '{path}' is not valid JSON: {exception.Message}", exception, ExitCodes.UsageError);
        }

        if (config?.Targets is null || config.Targets.Count is 0)
        {
            throw new AppCapException(
                $"Configuration file '{path}' does not define any targets. Add at least one entry under \"targets\".",
                ExitCodes.UsageError);
        }

        List<AppCapTargetConfig> applications = new(config.Targets.Count);
        foreach ((string name, AppCapTargetConfig? target) in config.Targets)
        {
            if (target is null || string.IsNullOrWhiteSpace(target.Id))
            {
                throw new AppCapException(
                    $"Target '{name}' in configuration file '{path}' is missing an \"id\" value.",
                    ExitCodes.UsageError);
            }

            if (target.Id.IndexOf('!', StringComparison.Ordinal) <= 0)
            {
                throw new AppCapException(
                    $"Target '{name}' in configuration file '{path}' has an invalid AUMID '{target.Id}'. Expected the form '<PackageFamilyName>!<ApplicationId>'.",
                    ExitCodes.UsageError);
            }

            target.Name = name;
            applications.Add(target);
        }

        return new TargetCatalog(applications);
    }
}
