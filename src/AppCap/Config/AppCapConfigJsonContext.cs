using System.Text.Json;
using System.Text.Json.Serialization;

namespace AppCap;

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppCapConfig))]
internal sealed partial class AppCapConfigJsonContext : JsonSerializerContext;
