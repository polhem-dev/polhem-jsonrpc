using Polhem.JsonRpc.Server;

namespace Polhem.JsonRpc.UnitTests;

/// <summary>
/// Creates <see cref="SpecTarget"/> for <c>Spec</c> and <see cref="DisposableTarget"/> for <c>Disposable</c>, and
/// disposes what it created.
/// </summary>
internal sealed class TestObjectFactory : IJsonRpcObjectFactory
{
    public object? CreateObject(string progId, JsonRpcRequestContext context) => progId switch
    {
        "Spec" => new SpecTarget(),
        "Disposable" => new DisposableTarget(),
        _ => null,
    };

    public ValueTask ReleaseObjectAsync(object instance, JsonRpcRequestContext context)
    {
        (instance as IDisposable)?.Dispose();
        return ValueTask.CompletedTask;
    }
}
