using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol;

namespace AppCap;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(CropRectangle))]
[JsonSerializable(typeof(CropRectangle?))]
internal sealed partial class McpToolsJsonContext : JsonSerializerContext;

internal static class McpToolSerialization
{
    public static JsonSerializerOptions SerializerOptions { get; } = CreateSerializerOptions();

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        JsonSerializerOptions options = new(McpJsonUtilities.DefaultOptions);
        options.TypeInfoResolverChain.Add(McpToolsJsonContext.Default);
        options.MakeReadOnly();
        return options;
    }
}