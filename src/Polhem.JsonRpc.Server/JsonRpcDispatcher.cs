using System.Collections.Concurrent;
using System.Collections.Frozen;
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
    private readonly ConcurrentDictionary<Type, Lazy<FrozenDictionary<string, MethodInfo>>> _actions = new();
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
    /// <exception cref="ArgumentException">A required setting is missing.</exception>
    /// <exception cref="InvalidOperationException">
    /// Code compiled against <c>Polhem.JsonRpc.Server</c> 1.0 is loaded, and
    /// <see cref="JsonRpcServerOptions.AllowCodeCompiledAgainst10"/> is not set.
    /// </exception>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public JsonRpcDispatcher(JsonRpcServerOptions options)
        : this(options, RequiredFactory(options))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonRpcDispatcher"/> class with an object factory of its own, which
    /// takes the place of <see cref="JsonRpcServerOptions.ObjectFactory"/>.
    /// </summary>
    /// <param name="options">The settings.</param>
    /// <param name="objectFactory">The factory that creates the object for the ProgId of a method name.</param>
    /// <remarks>
    /// The options are not changed, so several dispatchers, one per service provider for instance, can share them with
    /// a factory each.
    /// </remarks>
    /// <exception cref="ArgumentException">A required setting is missing.</exception>
    /// <exception cref="InvalidOperationException">
    /// Code compiled against <c>Polhem.JsonRpc.Server</c> 1.0 is loaded, and
    /// <see cref="JsonRpcServerOptions.AllowCodeCompiledAgainst10"/> is not set.
    /// </exception>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public JsonRpcDispatcher(JsonRpcServerOptions options, IJsonRpcObjectFactory objectFactory)
        : this(options, objectFactory, CompiledVersionGuard.FindStaleInProcess)
    {
    }

    // The check of loaded assemblies is passed in so that tests can give the constructor a stale assembly without
    // loading one, which would make every dispatcher of the test run refuse to start.
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    internal JsonRpcDispatcher(JsonRpcServerOptions options, IJsonRpcObjectFactory objectFactory, Func<IReadOnlyList<string>> findStale)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(objectFactory);
        ArgumentNullException.ThrowIfNull(findStale);
        if (!options.AllowCodeCompiledAgainst10) { CompiledVersionGuard.ThrowIfAny(findStale()); }
        var serializerOptions = options.SerializerOptions ?? throw new ArgumentException("SerializerOptions is required.", nameof(options));

        // `GetTypeInfo` does not fall back to reflection the way `JsonSerializer.Serialize(value, options)` does.
        _serializerOptions = serializerOptions.TypeInfoResolver is null
            ? new JsonSerializerOptions(serializerOptions) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() }
            : serializerOptions;
        _objectFactory = objectFactory;
        _policy = options.MethodPolicy ?? throw new ArgumentException("MethodPolicy is required.", nameof(options));
        _binder = options.ParameterBinder ?? new DefaultParameterBinder(_serializerOptions);
        _filters = [.. options.Filters];
        _exceptionMapper = options.ExceptionMapper;
        _includeExceptionDetails = options.IncludeExceptionDetails;
        _maxBatchSize = options.MaxBatchSize;
    }

    private static IJsonRpcObjectFactory RequiredFactory(JsonRpcServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.ObjectFactory
            ?? throw new ArgumentException("ObjectFactory is required: it creates the object for the ProgId of a method name.", nameof(options));
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
        var parsed = JsonRpcSerializer.ReadRequests(utf8Json, _maxBatchSize);

        var responses = new List<JsonRpcResponse>(parsed.Entries.Count);
        var messageItems = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var entry in parsed.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entry.IsValid)
            {
                responses.Add(JsonRpcResponse.Failure(entry.ErrorId, entry.Error));
                continue;
            }
            var response = await DispatchCoreAsync(entry.Request, transport, messageItems, cancellationToken).ConfigureAwait(false);
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
        var messageItems = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await DispatchCoreAsync(request, transport, messageItems, cancellationToken).ConfigureAwait(false);
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
    public Task<JsonRpcResponse?> DispatchAsync(JsonRpcRequest request, JsonRpcTransportInfo transport, CancellationToken cancellationToken = default)
        => DispatchCoreAsync(request, transport, messageItems: null, cancellationToken);

    private async Task<JsonRpcResponse?> DispatchCoreAsync(JsonRpcRequest request, JsonRpcTransportInfo transport,
        IDictionary<string, object?>? messageItems, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(transport);
        var context = new JsonRpcRequestContext(request, transport, cancellationToken) { SerializerOptions = _serializerOptions };
        if (messageItems is not null) { context.MessageItems = messageItems; }

        JsonRpcResponse? response = null;
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
            // the HTTP request instead of answering this one call. The release and the exception mapper are held to
            // the same rule; `DispatcherTests` pins all three with the `..._ReleaseThrows_...` and
            // `..._ExceptionMapperThrows_...` tests and `DispatchAsync_UnexpectedException_DoesNotLeakMessage`.
            response = JsonRpcResponse.Failure(request.Id, MapException(ex, context));
        }
        finally
        {
            if (instance is not null
                && await ReleaseObjectAsync(instance, context).ConfigureAwait(false) is { } releaseError
                && response is { IsSuccess: true })
            {
                // A call that failed keeps its own error; one that succeeded is answered with the release's.
                response = JsonRpcResponse.Failure(request.Id, releaseError);
            }
        }

        if (request.IsNotification) { return null; }
        return response!;
    }

    private async ValueTask<JsonRpcError?> ReleaseObjectAsync(object instance, JsonRpcRequestContext context)
    {
        try
        {
            await _objectFactory.ReleaseObjectAsync(instance, context).ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // NOTE: the release is the host's code as well, so its failure is answered like any other instead of
            // ending the batch, or the HTTP request, after its calls have already run.
            return MapException(ex, context);
        }
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

    // Exact, case-sensitive name match, like the Polhem framework's `Type.GetMethod(action)`. The table is built once
    // per object type, from the application's code only, so a name the caller makes up is looked up but never stored.
    // The table is built once per type under a Lazy, so the method policy is asked about each method once, not by every
    // request that happens to arrive first.
    private MethodInfo? FindAction(Type type, string action)
    {
        var table = _actions.GetOrAdd(type, static (key, self) => new Lazy<FrozenDictionary<string, MethodInfo>>(() => self.ResolveActions(key)), this);
        try
        {
            return table.Value.GetValueOrDefault(action);
        }
        catch (Exception)
        {
            // A Lazy keeps the exception of a build that failed, for example on an assembly that could not be loaded yet.
            // Dropping the entry lets the next call to the type try again instead of failing for the dispatcher's life.
            _actions.TryRemove(new KeyValuePair<Type, Lazy<FrozenDictionary<string, MethodInfo>>>(type, table));
            throw;
        }
    }

    // The suppression sits on this method rather than on `FindAction`, which only hands it to the cache as a
    // delegate: the reflection runs here.
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "The constructor requires unreferenced code; the object types come from the application's factory.")]
    private FrozenDictionary<string, MethodInfo> ResolveActions(Type type)
    {
        // A name with more than one resolvable method is ambiguous and not resolved; null marks it.
        var actions = new Dictionary<string, MethodInfo?>(StringComparer.Ordinal);
        foreach (var candidate in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!JsonRpcMethod.IsResolvableAction(candidate)) { continue; }
            actions[candidate.Name] = actions.ContainsKey(candidate.Name) ? null : candidate;
        }
        return actions
            .Where(pair => pair.Value is not null && IsCallable(pair.Value))
            .ToFrozenDictionary(pair => pair.Key, pair => pair.Value!, StringComparer.Ordinal);
    }

    private bool IsCallable(MethodInfo method)
    {
        try
        {
            return _policy.IsCallable(method);
        }
        catch (Exception)
        {
            // NOTE: the policy is the host's code. A method it cannot decide about is not callable, which keeps the
            // other methods of the type reachable instead of failing every call to the type.
            return false;
        }
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
        if (MapWithHostMapper(exception, context) is { } mapped) { return mapped; }

        JsonElement? data = _includeExceptionDetails
            ? JsonSerializer.SerializeToElement(exception.Message, JsonRpcServerJsonContext.Default.String)
            : null;
        return new JsonRpcError(JsonRpcErrorCodes.InternalError, InternalErrorMessage, data);
    }

    private JsonRpcError? MapWithHostMapper(Exception exception, JsonRpcRequestContext context)
    {
        try
        {
            return _exceptionMapper?.Invoke(exception, context);
        }
        catch (Exception)
        {
            // NOTE: a mapper that throws must not turn the failure of one call into the loss of the whole batch, so it
            // falls back to the default answer, which reveals the mapper's exception no more than the original's.
            return null;
        }
    }
}
