using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Polhem.JsonRpc.Client;

/// <summary>
/// Calls the methods of a JSON-RPC server.
/// </summary>
/// <remarks>
/// The connector is thread-safe and meant to be shared. It reads its <see cref="JsonRpcClientOptions"/> when it is
/// created, interceptors included, so later changes to them do not reach it. An error response is thrown as
/// <see cref="JsonRpcErrorException"/>, or as the exception <see cref="JsonRpcClientOptions.ErrorMapper"/> returns.
/// </remarks>
public sealed class JsonRpcConnector
{
    private readonly IJsonRpcTransport _transport;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly Func<JsonRpcId>? _idGenerator;
    private readonly TimeSpan? _timeout;
    private readonly Func<JsonRpcError, Exception?>? _errorMapper;
    private readonly IJsonRpcClientInterceptor[] _interceptors;
    private long _lastId;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonRpcConnector"/> class.
    /// </summary>
    /// <param name="transport">The transport, such as <see cref="HttpTransport"/>.</param>
    /// <param name="options">The settings, or <c>null</c> for the defaults.</param>
    public JsonRpcConnector(IJsonRpcTransport transport, JsonRpcClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _transport = transport;
        options ??= new JsonRpcClientOptions();
        _serializerOptions = WithResolver(options.SerializerOptions);
        _idGenerator = options.IdGenerator;
        _timeout = options.Timeout;
        _errorMapper = options.ErrorMapper;
        _interceptors = [.. options.Interceptors];
    }

    /// <summary>
    /// Calls a method and returns its result.
    /// </summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="method">The method name.</param>
    /// <param name="parameters">The parameters, serialized as the <c>params</c> object, or <c>null</c> for none.</param>
    /// <param name="cancellationToken">A token that cancels the call.</param>
    /// <returns>The result, or the default value when the method returns nothing.</returns>
    /// <exception cref="JsonRpcErrorException">The server answered with an error.</exception>
    public async Task<TResult?> InvokeAsync<TResult>(string method, object? parameters = null, CancellationToken cancellationToken = default)
        => ReadResult<TResult>(await CallAsync(method, parameters, cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Calls a method that returns nothing, and waits for the server to confirm it ran.
    /// </summary>
    /// <param name="method">The method name.</param>
    /// <param name="parameters">The parameters, or <c>null</c> for none.</param>
    /// <param name="cancellationToken">A token that cancels the call.</param>
    /// <returns>A task that completes when the server has answered.</returns>
    /// <exception cref="JsonRpcErrorException">The server answered with an error.</exception>
    public async Task InvokeAsync(string method, object? parameters, CancellationToken cancellationToken)
        => ThrowIfError(await CallAsync(method, parameters, cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Sends a notification: a call the server does not answer, so its outcome is not known.
    /// </summary>
    /// <param name="method">The method name.</param>
    /// <param name="parameters">The parameters, or <c>null</c> for none.</param>
    /// <param name="cancellationToken">A token that cancels the call.</param>
    /// <returns>A task that completes when the notification is sent.</returns>
    public async Task NotifyAsync(string method, object? parameters = null, CancellationToken cancellationToken = default)
    {
        var request = CreateRequest(method, parameters, isNotification: true);
        await SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts a batch: several calls sent in one message.
    /// </summary>
    /// <returns>The batch. Add calls to it, then send it with <see cref="JsonRpcBatch.SendAsync"/>.</returns>
    public JsonRpcBatch CreateBatch() => new(this);

    private async Task<JsonRpcResponse> CallAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        var request = CreateRequest(method, parameters, isNotification: false);
        var response = await SendAsync(request, cancellationToken).ConfigureAwait(false)
            ?? throw new JsonException("The server did not answer the request.");

        // A null id is accepted: a server answers a request it could not read under one, and a response without an id,
        // from a server that predates the specification, reads as one. Only the id of another request is refused.
        if (response.Id != request.Id && response.Id != JsonRpcId.Null)
        {
            throw new JsonException("The server answered with the id of another request.");
        }
        return response;
    }

    internal JsonRpcRequest CreateRequest(string method, object? parameters, bool isNotification)
    {
        ArgumentException.ThrowIfNullOrEmpty(method);
        JsonElement? element = null;
        if (parameters is not null)
        {
            var value = JsonSerializer.SerializeToElement(parameters, _serializerOptions.GetTypeInfo(parameters.GetType()));
            if (value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
            {
                throw new ArgumentException("Parameters must serialize to a JSON object or array.", nameof(parameters));
            }
            element = value;
        }
        var id = isNotification ? JsonRpcId.None : NextId();
        return new JsonRpcRequest(method, element, id);
    }

    internal async Task<JsonRpcResponse?> SendAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        using var timeout = CreateTimeout(cancellationToken);
        var token = timeout?.Token ?? cancellationToken;

        await BeforeSendAsync(request, token).ConfigureAwait(false);
        var response = await _transport.SendAsync(request, token).ConfigureAwait(false);
        if (response is not null)
        {
            await AfterReceiveAsync(request, response, token).ConfigureAwait(false);
        }
        return response;
    }

    internal async Task<IReadOnlyList<JsonRpcResponse>> SendBatchAsync(IReadOnlyList<JsonRpcRequest> requests, Func<JsonRpcResponse, JsonRpcRequest?> findRequest, CancellationToken cancellationToken)
    {
        using var timeout = CreateTimeout(cancellationToken);
        var token = timeout?.Token ?? cancellationToken;

        foreach (var request in requests)
        {
            await BeforeSendAsync(request, token).ConfigureAwait(false);
        }
        var responses = await _transport.SendBatchAsync(requests, token).ConfigureAwait(false);
        foreach (var response in responses)
        {
            if (findRequest(response) is { } request)
            {
                await AfterReceiveAsync(request, response, token).ConfigureAwait(false);
            }
        }
        return responses;
    }

    internal TResult? ReadResult<TResult>(JsonRpcResponse response)
    {
        ThrowIfError(response);
        if (response.Result is not { } result || result.ValueKind == JsonValueKind.Null)
        {
            return default;
        }

        // Handed over as it is: deserializing into a JsonElement would need the application's serializer context to
        // list JsonElement, which nothing tells an application under Native AOT to do.
        if (typeof(TResult) == typeof(JsonElement) || typeof(TResult) == typeof(JsonElement?))
        {
            return (TResult)(object)result;
        }
        return (TResult?)result.Deserialize(_serializerOptions.GetTypeInfo(typeof(TResult)));
    }

    private void ThrowIfError(JsonRpcResponse response)
    {
        if (response.Error is { } error)
        {
            throw _errorMapper?.Invoke(error) ?? new JsonRpcErrorException(error);
        }
    }

    // `GetTypeInfo` does not fall back to reflection the way `JsonSerializer.Serialize(value, options)` does, so
    // options without a resolver get the reflection-based one here, where reflection is allowed. Where it is not
    // (Native AOT, iOS), the options must carry a source-generated resolver; see `SerializerOptions`.
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Reached only when JsonSerializer.IsReflectionEnabledByDefault is true. Trimmed and AOT builds set that feature switch to false, and the trimmer removes the branch.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Reached only when JsonSerializer.IsReflectionEnabledByDefault is true. AOT builds set that feature switch to false, and the branch is removed.")]
    private static JsonSerializerOptions WithResolver(JsonSerializerOptions options)
    {
        if (options.TypeInfoResolver is not null || !JsonSerializer.IsReflectionEnabledByDefault) { return options; }
        return new JsonSerializerOptions(options) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
    }

    private JsonRpcId NextId()
    {
        if (_idGenerator is null) { return JsonRpcId.FromNumber(Interlocked.Increment(ref _lastId)); }
        var id = _idGenerator();
        return id.IsNone
            ? throw new InvalidOperationException("IdGenerator returned no id, which would make the call a notification.")
            : id;
    }

    private CancellationTokenSource? CreateTimeout(CancellationToken cancellationToken)
    {
        if (_timeout is not { } timeout) { return null; }
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(timeout);
        return source;
    }

    private async ValueTask BeforeSendAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        foreach (var interceptor in _interceptors)
        {
            await interceptor.OnRequestAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask AfterReceiveAsync(JsonRpcRequest request, JsonRpcResponse response, CancellationToken cancellationToken)
    {
        foreach (var interceptor in _interceptors)
        {
            await interceptor.OnResponseAsync(request, response, cancellationToken).ConfigureAwait(false);
        }
    }
}
