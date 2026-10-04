using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Polhem.JsonRpc.UnitTests;

public sealed record SubtractRequest(int Minuend, int Subtrahend);

public sealed record SubtractResponse(int Difference);

public sealed record NothingRequest(string Text);

public sealed record NothingResponse;

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

public sealed record DescribeRequest(string Text);

/// <summary>A parameter System.Text.Json cannot create: it throws <see cref="NotSupportedException"/>.</summary>
public abstract record AbstractRequest(string Text);

public sealed record AbstractResponse;

/// <summary>A parameter a JSON array would bind to, so only the binder's own rule refuses positional params.</summary>
[SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix",
    Justification = "The naming convention requires the name {Action}Request.")]
public sealed class NumbersRequest : List<int>;

public sealed record NumbersResponse(int Count);

/// <summary>
/// A response the default serializer options cannot write: System.Text.Json refuses <see cref="System.Type"/>.
/// </summary>
public sealed class DescribeResponse
{
    public Type Kind { get; set; } = typeof(string);

    public string Text { get; set; } = string.Empty;
}

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

    // Answers null, as a method with nothing to return does.
    public NothingResponse? Nothing(NothingRequest request) => null;

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

    public DescribeResponse Describe(DescribeRequest request) => new() { Text = request.Text };

    public AbstractResponse Abstract(AbstractRequest request) => new();

    public NumbersResponse Numbers(NumbersRequest request) => new(request.Count);

    // Not callable under the naming convention: the parameter is not named EchoRequest.
    public string Echo(EchoArgs args) => args.Text;

    // Not resolvable: static.
    public static SubtractResponse Static(SubtractRequest request) => new(0);

    // Not resolvable: two overloads make the name ambiguous.
    public SubtractResponse Twice(SubtractRequest request) => new(request.Minuend * 2);

    public SubtractResponse Twice(UpdateRequest request) => new(request.Text.Length * 2);

    // Not resolvable: `set_Label` is an accessor.
    public string Label { get; set; } = string.Empty;

    // Not resolvable: generic.
    public T Generic<T>(T value) => value;

    // The longest action name a method name may carry.
    public SubtractResponse LongActionNameThatFillsEveryOneOfTheSixtyFourCharactersAllowed_X(SubtractRequest request) =>
        new(request.Minuend - request.Subtrahend);

    // Not resolvable: one character over the limit.
    public SubtractResponse LongActionNameThatFillsEveryOneOfTheSixtyFourCharactersAllowed_XY(SubtractRequest request) =>
        new(request.Minuend - request.Subtrahend);
}

/// <summary>
/// A record, whose compiler-generated <c>Equals(RecordTarget?)</c> has the shape of an action. The ProgId is
/// <c>Record</c>.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "JSON-RPC actions are called on an instance; static methods are not resolved.")]
public record RecordTarget
{
    public SubtractResponse Subtract(SubtractRequest request) => new(request.Minuend - request.Subtrahend);
}

/// <summary>
/// A derived record: its <c>Equals(RecordTarget?)</c> overrides the base record's, and <c>Subtract</c> is inherited. The
/// ProgId is <c>DerivedRecord</c>.
/// </summary>
public sealed record DerivedRecordTarget : RecordTarget;

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
