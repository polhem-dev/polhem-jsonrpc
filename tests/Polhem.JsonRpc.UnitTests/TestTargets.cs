using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Polhem.JsonRpc.Server;

namespace Polhem.JsonRpc.UnitTests;

public sealed record SubtractArgs(int Minuend, int Subtrahend);

public sealed record EchoArgs(string Text);

/// <summary>
/// The methods of the JSON-RPC 2.0 specification's examples, plus shapes the dispatcher must handle.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "JSON-RPC targets are called on an instance; static methods are not resolved.")]
public sealed class SpecTarget
{
    // Tests run in parallel, so a test identifies its own call by a unique text instead of counting calls.
    public static ConcurrentBag<string> Updates { get; } = [];

    [JsonRpcMethod]
    public int Subtract(SubtractArgs args) => args.Minuend - args.Subtrahend;

    [JsonRpcMethod]
    public void Update(EchoArgs args) => Updates.Add(args.Text);

    [JsonRpcMethod]
    public static int StaticMethod(EchoArgs args) => args.Text.Length;

    public string Unmarked(EchoArgs args) => args.Text;

    [JsonRpcMethod]
    public string Twice(EchoArgs args) => args.Text + args.Text;

    [JsonRpcMethod]
    public string Twice(SubtractArgs args) => args.ToString();

    [JsonRpcMethod]
    public async Task<string> TaskResult(EchoArgs args)
    {
        await Task.Yield();
        return args.Text.ToUpperInvariant();
    }

    [JsonRpcMethod]
    public async ValueTask<int> ValueTaskResult(EchoArgs args)
    {
        await Task.Yield();
        return args.Text.Length;
    }

    [JsonRpcMethod]
    public async Task NoResult(EchoArgs args) => await Task.Yield();

    [JsonRpcMethod]
    public string Fail(EchoArgs args) => throw new InvalidOperationException("Secret connection string: " + args.Text);

    [JsonRpcMethod]
    public string Reject(EchoArgs args) => throw new JsonRpcErrorException(-32050, "Rejected: " + args.Text);

    [JsonRpcMethod]
    public string TransportKind(EchoArgs args) => args.Text;
}

/// <summary>
/// A target that records whether it was disposed.
/// </summary>
public sealed class DisposableTarget : IDisposable
{
    private string? _text;

    public static ConcurrentBag<string> Disposed { get; } = [];

    [JsonRpcMethod]
    public int Ping(EchoArgs args)
    {
        _text = args.Text;
        return args.Text.Length;
    }

    public void Dispose() => Disposed.Add(_text ?? string.Empty);
}
