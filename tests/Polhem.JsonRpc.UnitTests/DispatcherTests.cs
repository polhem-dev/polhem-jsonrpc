using System.Reflection;
using System.Text.Json;
using Polhem.JsonRpc.Server;

namespace Polhem.JsonRpc.UnitTests;

public partial class DispatcherTests
{
    private static async Task<JsonRpcResponse> CallAsync(JsonRpcDispatcher dispatcher, string method, string paramsJson, JsonRpcTransportInfo? transport = null)
    {
        var request = new JsonRpcRequest(method, DispatcherFixture.Element(paramsJson), JsonRpcId.FromNumber(1));
        var response = await dispatcher.DispatchAsync(request, transport ?? DispatcherFixture.Http());
        return response!;
    }

    [Fact(DisplayName = "The dispatcher requires an object factory")]
    public void Constructor_NoObjectFactory_Throws()
    {
        Assert.Throws<ArgumentException>(() => new JsonRpcDispatcher(new JsonRpcServerOptions()));
    }

    [Fact(DisplayName = "The object factory passed to the constructor takes the place of the one the options name")]
    public async Task Constructor_FactoryPassedAndInOptions_UsesPassed()
    {
        var inOptions = new RecordingFactory();
        var dispatcher = new JsonRpcDispatcher(new JsonRpcServerOptions { ObjectFactory = inOptions }, new TestObjectFactory());

        var response = await CallAsync(dispatcher, "Spec.Subtract", """{"minuend": 5, "subtrahend": 3}""");

        Assert.Equal(2, response.Result!.Value.GetProperty("difference").GetInt32());
        Assert.Empty(inOptions.Asked);
    }

    // Records the ProgIds it is asked for and creates what TestObjectFactory creates.
    private sealed class RecordingFactory : IJsonRpcObjectFactory
    {
        private readonly TestObjectFactory _inner = new();

        public List<string> Asked { get; } = [];

        public object? CreateObject(string progId, JsonRpcRequestContext context)
        {
            Asked.Add(progId);
            return _inner.CreateObject(progId, context);
        }

        public ValueTask ReleaseObjectAsync(object instance, JsonRpcRequestContext context) => _inner.ReleaseObjectAsync(instance, context);
    }

    private sealed class AllowAllPolicy : IJsonRpcMethodPolicy
    {
        public bool IsCallable(MethodInfo method) => true;
    }

    private sealed class RecordingFilter(string name, List<string> log) : IJsonRpcFilter
    {
        public async ValueTask InvokeAsync(JsonRpcRequestContext context, JsonRpcFilterDelegate next)
        {
            log.Add($"{name}:before");
            await next(context);
            log.Add($"{name}:after");
        }
    }

    private sealed class MethodRecordingFilter(List<string> seen) : IJsonRpcFilter
    {
        public ValueTask InvokeAsync(JsonRpcRequestContext context, JsonRpcFilterDelegate next)
        {
            var method = context.Method!;
            seen.Add($"{method.ProgId}.{method.Action}:{method.Instance.GetType().Name}");
            return next(context);
        }
    }

    private sealed class RejectingFilter : IJsonRpcFilter
    {
        public ValueTask InvokeAsync(JsonRpcRequestContext context, JsonRpcFilterDelegate next) =>
            throw new JsonRpcErrorException(-32001, "Unauthorized");
    }

    private sealed class SealedPayloadFilter : IJsonRpcFilter
    {
        public async ValueTask InvokeAsync(JsonRpcRequestContext context, JsonRpcFilterDelegate next)
        {
            context.Request.Params = SealedPayload.Open(context.Request.Params!.Value);
            await next(context);
            context.Result = SealedPayload.Seal(context.GetResult()!.Value);
        }
    }

    private sealed class DescribeFilter : IJsonRpcFilter
    {
        public async ValueTask InvokeAsync(JsonRpcRequestContext context, JsonRpcFilterDelegate next)
        {
            await next(context);
            var response = (DescribeResponse)context.ReturnValue!;
            context.Result = JsonSerializer.SerializeToElement($"{response.Kind.Name}:{response.Text}");
        }
    }

    private sealed class ThrowingReleaseFactory : IJsonRpcObjectFactory
    {
        private readonly TestObjectFactory _inner = new();

        public object? CreateObject(string progId, JsonRpcRequestContext context) => _inner.CreateObject(progId, context);

        public ValueTask ReleaseObjectAsync(object instance, JsonRpcRequestContext context) =>
            throw new InvalidOperationException("Release failed");
    }

    // Lets the call run, then cancels, as a client that disconnects during a batch.
    private sealed class CancelAfterCallFilter(CancellationTokenSource cancellation) : IJsonRpcFilter
    {
        public async ValueTask InvokeAsync(JsonRpcRequestContext context, JsonRpcFilterDelegate next)
        {
            await next(context);
            await cancellation.CancelAsync();
        }
    }

    // Throws when the token is cancelled; with a live token it throws a cancellation the caller never asked for.
    private sealed class CancellationObservingFilter : IJsonRpcFilter
    {
        public ValueTask InvokeAsync(JsonRpcRequestContext context, JsonRpcFilterDelegate next)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            throw new OperationCanceledException();
        }
    }

    private sealed class KindRecordingFilter(List<JsonRpcTransportKind> seen) : IJsonRpcFilter
    {
        public ValueTask InvokeAsync(JsonRpcRequestContext context, JsonRpcFilterDelegate next)
        {
            seen.Add(context.Transport.Kind);
            return next(context);
        }
    }

    private sealed class TransportRecordingFilter(List<string> seen) : IJsonRpcFilter
    {
        public ValueTask InvokeAsync(JsonRpcRequestContext context, JsonRpcFilterDelegate next)
        {
            seen.Add($"{context.Transport.Kind}:{context.Items["token"]}");
            return next(context);
        }
    }
}
