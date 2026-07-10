namespace AppCap;

public sealed class TargetCatalog
{
    private readonly TargetApplication[] applications;
    private readonly Dictionary<string, TargetApplication> applicationsByName;

    public TargetCatalog(IReadOnlyList<TargetApplication> applications)
    {
        ArgumentNullException.ThrowIfNull(applications);
        if (applications.Count is 0)
        {
            throw new ArgumentException("At least one target application is required.", nameof(applications));
        }

        this.applications = applications.ToArray();

        applicationsByName = new Dictionary<string, TargetApplication>(StringComparer.Ordinal);
        foreach (TargetApplication application in this.applications)
        {
            applicationsByName[application.Name] = application;
        }

        Default = this.applications[0];
    }

    public IReadOnlyList<TargetApplication> Applications => applications;

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
