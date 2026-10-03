using Polhem.JsonRpc.Server;

namespace PayloadQuickStart.Server;

/// <summary>
/// Creates a <see cref="Calculator"/> for the ProgId <c>Calculator</c>.
/// </summary>
public sealed class AppObjectFactory : IJsonRpcObjectFactory
{
    public object? CreateObject(string progId, JsonRpcRequestContext context)
        => progId == "Calculator" ? new Calculator() : null;
}
