using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Polhem.JsonRpc.UnitTests;

public sealed record SubtractRequest(int Minuend, int Subtrahend);

public sealed record SubtractResponse(int Difference);

public sealed record UpdateRequest(string Text);

public sealed record UpdateResponse;

public sealed record UpperRequest(string Text);

public sealed record UpperResponse(string Text);

public sealed record LengthRequest(string Text);

public sealed record LengthResponse(int Length);

public sealed record FailRequest(string Text);

public sealed record FailResponse;

public sealed record RejectRequest(string Text);

public sealed record RejectResponse;

public sealed record MismatchRequest(string Text);

public sealed record EchoArgs(string Text);

/// <summary>
/// The methods of the JSON-RPC 2.0 specification's examples, plus shapes the dispatcher must handle. The ProgId
/// is <c>Spec</c>.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "JSON-RPC actions are called on an instance; static methods are not resolved.")]
public sealed class SpecTarget
{
    // Tests run in parallel, so a test identifies its own call by a unique text instead of counting calls.
    public static ConcurrentBag<string> Updates { get; } = [];

    public SubtractResponse Subtract(SubtractRequest request) => new(request.Minuend - request.Subtrahend);

    public UpdateResponse Update(UpdateRequest request)
    {
        Updates.Add(request.Text);
        return new UpdateResponse();
    }

    public async Task<UpperResponse> Upper(UpperRequest request)
    {
        await Task.Yield();
        return new UpperResponse(request.Text.ToUpperInvariant());
    }

    public async ValueTask<LengthResponse> Length(LengthRequest request)
    {
        await Task.Yield();
        return new LengthResponse(request.Text.Length);
    }

    public FailResponse Fail(FailRequest request) => throw new InvalidOperationException("Secret connection string: " + request.Text);

    public RejectResponse Reject(RejectRequest request) => throw new JsonRpcErrorException(-32050, "Rejected: " + request.Text);

    // Not callable under the naming convention: the result is not named MismatchResponse.
    public FailResponse Mismatch(MismatchRequest request) => new();

    // Not callable under the naming convention: the parameter is not named EchoRequest.
    public string Echo(EchoArgs args) => args.Text;

    // Not resolvable: static.
    public static SubtractResponse Static(SubtractRequest request) => new(0);

    // Not resolvable: two overloads make the name ambiguous.
    public SubtractResponse Twice(SubtractRequest request) => new(request.Minuend * 2);

    public SubtractResponse Twice(UpdateRequest request) => new(request.Text.Length * 2);
}

/// <summary>
/// An object that records whether it was released. The ProgId is <c>Disposable</c>.
/// </summary>
public sealed class DisposableTarget : IDisposable
{
    private string? _text;

    public static ConcurrentBag<string> Disposed { get; } = [];

    public UpdateResponse Update(UpdateRequest request)
    {
        _text = request.Text;
        return new UpdateResponse();
    }

    public FailResponse Fail(FailRequest request)
    {
        _text = request.Text;
        throw new InvalidOperationException("Failed");
    }

    public void Dispose() => Disposed.Add(_text ?? string.Empty);
}
