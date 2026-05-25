using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace D365FO.Core;

public static class D365Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    public static readonly JsonSerializerOptions Pretty = new(Options) { WriteIndented = true };

    public static string Serialize<T>(T value, bool indented = false)
        => JsonSerializer.Serialize(value, indented ? Pretty : Options);
}
