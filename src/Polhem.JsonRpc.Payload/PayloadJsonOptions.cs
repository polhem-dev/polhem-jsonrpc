using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Polhem.JsonRpc.Payload;

/// <summary>
/// Prepares the <see cref="JsonSerializerOptions"/> the package serializes with.
/// </summary>
internal static class PayloadJsonOptions
{
    // `GetTypeInfo` does not fall back to reflection the way `JsonSerializer.Serialize(value, options)` does, so
    // options without a resolver get the reflection-based one here, where reflection is allowed. Where it is not
    // (Native AOT, iOS), the options must carry a source-generated resolver.
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Reached only when JsonSerializer.IsReflectionEnabledByDefault is true. Trimmed and AOT builds set that feature switch to false, and the trimmer removes the branch.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Reached only when JsonSerializer.IsReflectionEnabledByDefault is true. AOT builds set that feature switch to false, and the branch is removed.")]
    public static JsonSerializerOptions WithResolver(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.TypeInfoResolver is not null || !JsonSerializer.IsReflectionEnabledByDefault) { return options; }
        return new JsonSerializerOptions(options) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
    }
}
