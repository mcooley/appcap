using System.Text.Json.Serialization;

namespace AppCap;

public sealed class AppCapConfig
{
    public Dictionary<string, AppCapTargetConfig>? Targets { get; set; }
}

public sealed class AppCapTargetConfig
{
    // The name of the target. Populated from the configuration key; not part of the JSON body.
    [JsonIgnore]
    public string Name { get; set; } = string.Empty;

    // The ID of the application.
    // On Windows, this is the application's AUMID (Application User Model ID).
    public string Id { get; set; } = string.Empty;

    // The package family name, derived from the application ID (AUMID).
    [JsonIgnore]
    public string PackageFamilyName
    {
        get
        {
            int separator = Id.IndexOf('!', StringComparison.Ordinal);
            return separator > 0 ? Id[..separator] : Id;
        }
    }
}
