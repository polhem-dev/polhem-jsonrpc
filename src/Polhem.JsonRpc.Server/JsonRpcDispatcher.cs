using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Polhem.JsonRpc.Server;

/// <summary>
/// Runs JSON-RPC calls. For a method name <c>ProgId.Action</c> it creates the object for the ProgId, finds the
/// action on it, checks the method policy, runs the filters, binds the parameters, invokes the method and turns the
/// outcome into a response. It does not know how the calls arrive.
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

    private readonly IJsonRpcObjectFactory _objectFactory;
    private readonly IJsonRpcMethodPolicy _policy;
    private readonly ConcurrentDictionary<(Type Type, string Action), MethodInfo?> _actions = new();
    private readonly IJsonRpcParameterBinder _binder;
    private readonly IJsonRpcFilter[] _filters;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly Func<Exception, JsonRpcRequestContext, JsonRpcError?>? _exceptionMapper;
    private readonly bool _includeExceptionDetails;
    private readonly int _maxBatchSize;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonRpcDispatcher"/> class.
    /// </summary>
    /// <param name="options">The settings. <see cref="JsonRpcServerOptions.ObjectFactory"/> is required.</param>
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
        _objectFactory = options.ObjectFactory
            ?? throw new ArgumentException("ObjectFactory is required: it creates the object for the ProgId of a method name.", nameof(options));
        _policy = options.MethodPolicy ?? throw new ArgumentException("MethodPolicy is required.", nameof(options));
        _binder = options.ParameterBinder ?? new DefaultParameterBinder(_serializerOptions);
        _filters = [.. options.Filters];
        _exceptionMapper = options.ExceptionMapper;
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
        var context = new JsonRpcRequestContext(request, transport, cancellationToken) { SerializerOptions = _serializerOptions };

        JsonRpcResponse response;
        object? instance = null;
        try
        {
            // The order follows the Polhem framework: the object and the method are resolved, and the method policy
            // checked, before any filter runs, so that a filter which decrypts the parameters only does so for a
            // call that is allowed to happen.
            if (!JsonRpcMethodName.TryParse(request.Method, out var progId, out var action))
            {
                throw new JsonRpcErrorException(JsonRpcErrorCodes.MethodNotFound, MethodNotFoundMessage);
            }
            instance = _objectFactory.CreateObject(progId, context)
                ?? throw new JsonRpcErrorException(JsonRpcErrorCodes.MethodNotFound, MethodNotFoundMessage);
            var method = FindAction(instance.GetType(), action)
                ?? throw new JsonRpcErrorException(JsonRpcErrorCodes.MethodNotFound, MethodNotFoundMessage);
            context.Method = new JsonRpcMethod(progId, action, instance, method);

            await RunFiltersAsync(context, 0).ConfigureAwait(false);
            response = JsonRpcResponse.Success(request.Id, context.GetResult());
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
        finally
        {
            if (instance is not null)
            {
                await _objectFactory.ReleaseObjectAsync(instance, context).ConfigureAwait(false);
            }
        }

        if (request.IsNotification) { return null; }
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
        var (value, valueType) = await InvokeMethodAsync(method.MethodInfo, method.Instance, argument).ConfigureAwait(false);
        context.ReturnValue = value;
        context.ReturnType = valueType;
    }

    // Exact, case-sensitive name match, like the Polhem framework's `Type.GetMethod(action)`. A name with more than
    // one resolvable method is ambiguous and not resolved.
    private MethodInfo? FindAction(Type type, string action) => _actions.GetOrAdd((type, action), ResolveAction);

    // The suppression sits on this method rather than on `FindAction`: a lambda compiles to a method of its own,
    // which a suppression on the method that declares it does not cover.
    [UnconditionalSuppressMessage("Trimming", "IL2080",
        Justification = "The constructor requires unreferenced code; the object types come from the application's factory.")]
    private MethodInfo? ResolveAction((Type Type, string Action) key)
    {
        MethodInfo? found = null;
        foreach (var candidate in key.Type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!string.Equals(candidate.Name, key.Action, StringComparison.Ordinal) || !JsonRpcMethod.IsResolvableAction(candidate))
            {
                continue;
            }
            if (found is not null) { return null; }
            found = candidate;
        }
        return found is not null && _policy.IsCallable(found) ? found : null;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "The constructor requires unreferenced code; Result is read from the task type the method declares.")]
    private static async ValueTask<(object? Value, Type? ValueType)> InvokeMethodAsync(MethodInfo method, object instance, object? argument)
    {
        object? returned;
        try
        {
            returned = method.Invoke(instance, [argument]);
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
        return new JsonRpcError(JsonRpcErrorCodes.InternalError, InternalErrorMessage, data);
    }
}
