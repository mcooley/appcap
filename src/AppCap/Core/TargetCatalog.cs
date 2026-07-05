namespace AppCap;

public sealed class TargetCatalog
{
    private readonly Dictionary<string, TargetApplication> applicationsByName;

    public TargetCatalog(IReadOnlyList<TargetApplication> applications)
    {
        ArgumentNullException.ThrowIfNull(applications);
        if (applications.Count is 0)
        {
            throw new ArgumentException("At least one target application is required.", nameof(applications));
        }

        applicationsByName = new Dictionary<string, TargetApplication>(StringComparer.Ordinal);
        foreach (TargetApplication application in applications)
        {
            applicationsByName[application.Name] = application;
        }

        Default = applications[0];
    }

    public TargetApplication Default { get; }

    public bool TryParse(string value, out TargetApplication target)
    {
        if (value is not null && applicationsByName.TryGetValue(value, out TargetApplication? application))
        {
            target = application;
            return true;
        }

        target = Default;
        return false;
    }
}
