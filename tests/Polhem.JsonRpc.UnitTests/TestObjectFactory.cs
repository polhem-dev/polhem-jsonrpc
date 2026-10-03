using Polhem.JsonRpc.Server;

namespace Polhem.JsonRpc.UnitTests;

/// <summary>
/// Creates <see cref="SpecTarget"/> for <c>Spec</c> and <see cref="DisposableTarget"/> for <c>Disposable</c>, and
/// disposes what it created.
/// </summary>
internal sealed class TestObjectFactory : IJsonRpcObjectFactory
{
    // The longest ProgId a method name may carry; one character more is not a method name.
    public const string LongProgId = "Long-ProgId_That_Fills_Every_One_Of_The_64_Characters_Allowed-XY";

    public object? CreateObject(string progId, JsonRpcRequestContext context) => progId switch
    {
        "Spec" => new SpecTarget(),
        "Disposable" => new DisposableTarget(),
        "Record" => new RecordTarget(),
        "DerivedRecord" => new DerivedRecordTarget(),
        LongProgId => new SpecTarget(),
        LongProgId + "X" => new SpecTarget(),
        _ => null,
    };

    public ValueTask ReleaseObjectAsync(object instance, JsonRpcRequestContext context)
    {
        (instance as IDisposable)?.Dispose();
        return ValueTask.CompletedTask;
    }
}
