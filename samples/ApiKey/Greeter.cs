using Polhem.JsonRpc.Server;

namespace ApiKey;

/// <summary>
/// The method behind <c>greeter.hello</c>.
/// </summary>
public sealed class Greeter
{
    [JsonRpcMethod]
    public string Hello(HelloArgs args) => $"Hello, {args.Name}!";
}

public sealed record HelloArgs(string Name);
