using System.Text.Json.Serialization;

namespace Polhem.JsonRpc.AotSmoke;

[JsonSerializable(typeof(AddArgs))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(PayloadArgs))]
internal sealed partial class SmokeJsonContext : JsonSerializerContext
{
}
