using System.Text.Json.Serialization;

namespace Polhem.JsonRpc.Server;

/// <summary>
/// Source-generated metadata for the values the dispatcher writes itself.
/// </summary>
[JsonSerializable(typeof(string))]
internal sealed partial class JsonRpcServerJsonContext : JsonSerializerContext
{
}
