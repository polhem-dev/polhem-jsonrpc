using Polhem.JsonRpc.Server;

namespace QuickStart.Server;

/// <summary>
/// Creates the object for the ProgId of a method name: <c>Calculator.Add</c> runs <c>Add</c> on a new
/// <see cref="Calculator"/>.
/// </summary>
public sealed class AppObjectFactory : IJsonRpcObjectFactory
{
    public object? CreateObject(string progId, JsonRpcRequestContext context) => progId switch
    {
        "Calculator" => new Calculator(),
        _ => null,
    };
}
