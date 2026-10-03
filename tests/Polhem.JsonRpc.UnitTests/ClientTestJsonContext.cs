using System.Text.Json.Serialization;

namespace Polhem.JsonRpc.UnitTests;

/// <summary>
/// A source-generated context that lists the request type only, as an application under Native AOT would, so a test can
/// show what the client reads without metadata of its own.
/// </summary>
[JsonSerializable(typeof(SubtractRequest))]
internal sealed partial class ClientTestJsonContext : JsonSerializerContext
{
}
