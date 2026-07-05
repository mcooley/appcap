namespace AppCap;

public sealed class TargetCatalog
{
    private readonly Dictionary<string, AppCapTargetConfig> applicationsByName;

    public TargetCatalog(IReadOnlyList<AppCapTargetConfig> applications)
    {
        ArgumentNullException.ThrowIfNull(applications);

        applicationsByName = new Dictionary<string, AppCapTargetConfig>(StringComparer.Ordinal);
        foreach (AppCapTargetConfig application in applications)
        {
            applicationsByName[application.Name] = application;
        }

        Default = new TargetConfiguration("default", applications);
    }

    public TargetConfiguration Default { get; }

    public bool TryParse(string value, out TargetConfiguration target)
    {
        if (value is not null && applicationsByName.TryGetValue(value, out AppCapTargetConfig? application))
        {
            target = new TargetConfiguration(value, [application]);
            return true;
        }

        target = Default;
        return false;
    }
}
