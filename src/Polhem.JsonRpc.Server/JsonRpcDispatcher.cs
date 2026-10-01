using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Polhem.JsonRpc.Server;

/// <summary>
/// Runs JSON-RPC calls: resolves the method, runs the filters, binds the parameters, invokes the method and turns
/// the outcome into a response. It does not know how the calls arrive.
/// </summary>
/// <remarks>
/// The dispatcher is thread-safe and meant to be shared. The options are read when it is created; later changes to
/// them have no effect.
/// </remarks>
public sealed class JsonRpcDispatcher
{
    internal const string ReflectionMessage =
        "The dispatcher resolves methods and binds parameters by reflection, which trimming and Native AOT do not support.";

    private const string InternalErrorMessage = "Internal error";
    private const string MethodNotFoundMessage = "Method not found";
    private const string InvalidRequestMessage = "Invalid Request";

    private readonly IJsonRpcMethodResolver _resolver;
    private readonly IJsonRpcTargetFactory _targetFactory;
    private readonly IJsonRpcParameterBinder _binder;
    private readonly IJsonRpcFilter[] _filters;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly Func<Exception, JsonRpcRequestContext, JsonRpcError?>? _exceptionMapper;
    private readonly int _internalErrorCode;
    private readonly bool _includeExceptionDetails;
    private readonly int _maxBatchSize;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonRpcDispatcher"/> class.
    /// </summary>
    /// <param name="options">The settings.</param>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public JsonRpcDispatcher(JsonRpcServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var serializerOptions = options.SerializerOptions ?? throw new ArgumentException("SerializerOptions is required.", nameof(options));

        // `GetTypeInfo` does not fall back to reflection the way `JsonSerializer.Serialize(value, options)` does.
        _serializerOptions = serializerOptions.TypeInfoResolver is null
            ? new JsonSerializerOptions(serializerOptions) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() }
            : serializerOptions;
        _resolver = options.MethodResolver
            ?? new ConventionMethodResolver(options.Targets, options.MethodPolicy);
        _targetFactory = options.TargetFactory ?? new DefaultTargetFactory();
        _binder = options.ParameterBinder ?? new DefaultParameterBinder(_serializerOptions);
        _filters = [.. options.Filters];
        _exceptionMapper = options.ExceptionMapper;
        _internalErrorCode = options.InternalErrorCode;
        _includeExceptionDetails = options.IncludeExceptionDetails;
        _maxBatchSize = options.MaxBatchSize;
    }

    /// <summary>
    /// Runs an incoming message: a single request or a batch, as UTF-8 JSON.
    /// </summary>
    /// <param name="utf8Json">The message.</param>
    /// <param name="transport">What the transport knows about the call.</param>
    /// <param name="cancellationToken">A token that cancels the call.</param>
    /// <returns>The answer.</returns>
    public async Task<JsonRpcDispatchResult> DispatchMessageAsync(ReadOnlyMemory<byte> utf8Json, JsonRpcTransportInfo transport, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var parsed = JsonRpcSerializer.ReadRequests(utf8Json);

        if (parsed.IsBatch && parsed.Entries.Count > _maxBatchSize)
        {
            var tooLarge = JsonRpcResponse.Failure(JsonRpcId.Null, new JsonRpcError(JsonRpcErrorCodes.InvalidRequest, InvalidRequestMessage));
            return new JsonRpcDispatchResult(isBatch: false, [tooLarge]);
        }

        var responses = new List<JsonRpcResponse>(parsed.Entries.Count);
        foreach (var entry in parsed.Entries)
        {
            if (!entry.IsValid)
            {
                responses.Add(JsonRpcResponse.Failure(entry.ErrorId, entry.Error));
                continue;
            }
            var response = await DispatchAsync(entry.Request, transport, cancellationToken).ConfigureAwait(false);
            if (response is not null) { responses.Add(response); }
        }
        return new JsonRpcDispatchResult(parsed.IsBatch, responses);
    }

    /// <summary>
    /// Runs a batch of requests.
    /// </summary>
    /// <param name="requests">The requests.</param>
    /// <param name="transport">What the transport knows about the call.</param>
    /// <param name="cancellationToken">A token that cancels the call.</param>
    /// <returns>The responses; notifications have none.</returns>
    public async Task<IReadOnlyList<JsonRpcResponse>> DispatchBatchAsync(IReadOnlyList<JsonRpcRequest> requests, JsonRpcTransportInfo transport, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(transport);
        if (requests.Count == 0 || requests.Count > _maxBatchSize)
        {
            return [JsonRpcResponse.Failure(JsonRpcId.Null, new JsonRpcError(JsonRpcErrorCodes.InvalidRequest, InvalidRequestMessage))];
        }

        var responses = new List<JsonRpcResponse>(requests.Count);
        foreach (var request in requests)
        {
            var response = await DispatchAsync(request, transport, cancellationToken).ConfigureAwait(false);
            if (response is not null) { responses.Add(response); }
        }
        return responses;
    }

    /// <summary>
    /// Runs one request.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="transport">What the transport knows about the call.</param>
    /// <param name="cancellationToken">A token that cancels the call.</param>
    /// <returns>The response, or <c>null</c> for a notification, which is not answered even when it fails.</returns>
    public async Task<JsonRpcResponse?> DispatchAsync(JsonRpcRequest request, JsonRpcTransportInfo transport, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(transport);
        var context = new JsonRpcRequestContext(request, transport, cancellationToken);

        JsonRpcResponse response;
        try
        {
            context.Method = _resolver.Resolve(context)
                ?? throw new JsonRpcErrorException(JsonRpcErrorCodes.MethodNotFound, MethodNotFoundMessage);
            await RunFiltersAsync(context, 0).ConfigureAwait(false);
            response = JsonRpcResponse.Success(request.Id, context.Result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // NOTE: this is the boundary between the caller and the host's code. Every failure, including one the
            // host did not foresee, has to become an error response; letting it escape would end the whole batch or
            // the HTTP request instead of answering this one call.
            response = JsonRpcResponse.Failure(request.Id, MapException(ex, context));
        }

        if (request.IsNotification) { return null; }
        if (context.HasResponseMembers)
        {
            response.AdditionalMembers = new Dictionary<string, JsonElement>(context.ResponseMembers, StringComparer.Ordinal);
        }
        return response;
    }

    private ValueTask RunFiltersAsync(JsonRpcRequestContext context, int index)
    {
        if (index == _filters.Length) { return InvokeAsync(context); }
        return _filters[index].InvokeAsync(context, nextContext => RunFiltersAsync(nextContext, index + 1));
    }

    private async ValueTask InvokeAsync(JsonRpcRequestContext context)
    {
        var method = context.Method!;
        var argument = _binder.Bind(context);
        var target = _targetFactory.CreateTarget(method, context);
        try
        {
            var (value, valueType) = await InvokeMethodAsync(method.MethodInfo, target, argument).ConfigureAwait(false);
            context.Result = valueType is null || value is null
                ? null
                : JsonSerializer.SerializeToElement(value, _serializerOptions.GetTypeInfo(valueType));
        }
        finally
        {
            await _targetFactory.ReleaseTargetAsync(target, context).ConfigureAwait(false);
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "The constructor requires unreferenced code; Result is read from the task type the method declares.")]
    private static async ValueTask<(object? Value, Type? ValueType)> InvokeMethodAsync(MethodInfo method, object target, object? argument)
    {
        object? returned;
        try
        {
            returned = method.Invoke(target, [argument]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }

        var returnType = method.ReturnType;
        if (returnType == typeof(void)) { return (null, null); }

        Task? task = returned switch
        {
            Task t => t,
            ValueTask vt => vt.AsTask(),
            _ => null,
        };
        if (task is null && returned is not null && returnType.IsGenericType
            && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            task = (Task)returnType.GetMethod(nameof(ValueTask<int>.AsTask))!.Invoke(returned, null)!;
        }
        if (task is null) { return (returned, returnType); }

        await task.ConfigureAwait(false);
        var taskType = task.GetType();
        if (!returnType.IsGenericType) { return (null, null); }
        var valueType = returnType.GetGenericArguments()[0];
        return (taskType.GetProperty(nameof(Task<int>.Result))!.GetValue(task), valueType);
    }

    private JsonRpcError MapException(Exception exception, JsonRpcRequestContext context)
    {
        if (exception is JsonRpcErrorException rpc) { return rpc.Error; }
        if (_exceptionMapper?.Invoke(exception, context) is { } mapped) { return mapped; }

        JsonElement? data = _includeExceptionDetails
            ? JsonSerializer.SerializeToElement(exception.Message, JsonRpcServerJsonContext.Default.String)
            : null;
        return new JsonRpcError(_internalErrorCode, InternalErrorMessage, data);
    }
}
