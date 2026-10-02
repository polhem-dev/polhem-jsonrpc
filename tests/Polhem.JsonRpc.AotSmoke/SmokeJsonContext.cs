using System.Text.Json.Serialization;

namespace Polhem.JsonRpc.AotSmoke;

[JsonSerializable(typeof(AddArgs))]
[JsonSerializable(typeof(int))]
internal sealed partial class SmokeJsonContext : JsonSerializerContext
{
}
