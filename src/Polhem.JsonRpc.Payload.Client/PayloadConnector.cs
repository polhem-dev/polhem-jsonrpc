using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Polhem.JsonRpc.Client;

namespace Polhem.JsonRpc.Payload.Client;

/// <summary>
/// Calls the methods of a JSON-RPC server that uses the payload envelope: it seals the parameters of each call and opens
/// its result, so an encrypted call is made the way a plain call is made with <see cref="JsonRpcConnector"/>.
/// </summary>
/// <remarks>
/// The parameters are sealed with <see cref="PayloadProcessor.WrapRequest"/>, in the format of the call, with the codec
/// of <see cref="PayloadConnectorOptions"/>, the key <see cref="PayloadConnectorOptions.KeyProvider"/> returns and the
/// next sequence number. The result is opened with <see cref="PayloadProcessor.UnwrapResult{T}"/> for the same method
/// and format. Errors are thrown as <see cref="JsonRpcConnector"/> throws them.
/// <para>
/// The connector is thread-safe and meant to be shared by the calls of one key and one replay scope.
/// </para>
/// </remarks>
public sealed class PayloadConnector
{
    private readonly JsonRpcConnector _connector;
    private readonly PayloadProcessor _processor;
    private readonly PayloadFormat _format;
    private readonly string? _codec;
    private readonly Func<byte[]?>? _keyProvider;
    private readonly Func<long>? _sequenceGenerator;
    private long _lastSequence;

    /// <summary>
    /// Initializes a new instance of the <see cref="PayloadConnector"/> class.
    /// </summary>
    /// <param name="connector">The connector the calls are sent through.</param>
    /// <param name="processor">The processor that seals the parameters and opens the results, with the settings the
    /// server uses.</param>
    /// <param name="options">The settings, or <see langword="null"/> for the defaults.</param>
    public PayloadConnector(JsonRpcConnector connector, PayloadProcessor processor, PayloadConnectorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(connector);
        ArgumentNullException.ThrowIfNull(processor);
        options ??= new PayloadConnectorOptions();
        _connector = connector;
        _processor = processor;
        _format = options.Format;
        _codec = options.Codec;
        _keyProvider = options.KeyProvider;
        _sequenceGenerator = options.SequenceGenerator;
    }

    /// <summary>
    /// Calls a method in the format of <see cref="PayloadConnectorOptions.Format"/> and returns its result.
    /// </summary>
    /// <typeparam name="TResult">The type of the result. <see cref="object"/> opens the result into the type its
    /// envelope names, which must be registered with <see cref="PayloadOptions.TypeResolver"/>.</typeparam>
    /// <param name="method">The method name.</param>
    /// <param name="parameters">The parameters. Only a plain call may pass <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token that cancels the call.</param>
    /// <returns>The result, or the default value when the method returns nothing.</returns>
    /// <exception cref="JsonRpcErrorException">The server answered with an error.</exception>
    /// <exception cref="InvalidOperationException">
    /// The call is encrypted and there is no key, or the parameters are <see langword="null"/> in a call that is not
    /// plain, or they could not be encoded.
    /// </exception>
    /// <exception cref="InvalidPayloadException">The result is not an envelope, or not in the format of the call.</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">An encrypted result fails authentication.</exception>
    [SuppressMessage("ApiDesign", "RS0027:API with optional parameter(s) should have the most parameters amongst its public overloads",
        Justification = "The signature is JsonRpcConnector.InvokeAsync<TResult>, so that a plain call and a sealed one are written alike.")]
    public Task<TResult?> InvokeAsync<TResult>(string method, object? parameters = null, CancellationToken cancellationToken = default)
        => InvokeAsync<TResult>(method, parameters, _format, cancellationToken);

    /// <summary>
    /// Calls a method in the given format and returns its result.
    /// </summary>
    /// <typeparam name="TResult">The type of the result. <see cref="object"/> opens the result into the type its
    /// envelope names, which must be registered with <see cref="PayloadOptions.TypeResolver"/>.</typeparam>
    /// <param name="method">The method name.</param>
    /// <param name="parameters">The parameters. Only a plain call may pass <see langword="null"/>.</param>
    /// <param name="format">The format of this call.</param>
    /// <param name="cancellationToken">A token that cancels the call.</param>
    /// <returns>The result, or the default value when the method returns nothing.</returns>
    /// <exception cref="JsonRpcErrorException">The server answered with an error.</exception>
    /// <exception cref="InvalidOperationException">
    /// The call is encrypted and there is no key, or the parameters are <see langword="null"/> in a call that is not
    /// plain, or they could not be encoded.
    /// </exception>
    /// <exception cref="InvalidPayloadException">The result is not an envelope, or not in the format of the call.</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">An encrypted result fails authentication.</exception>
    public async Task<TResult?> InvokeAsync<TResult>(string method, object? parameters, PayloadFormat format, CancellationToken cancellationToken)
    {
        var key = KeyFor(format);
        var sealedParameters = Wrap(method, parameters, format, key);
        var result = await _connector.InvokeAsync<JsonElement?>(method, sealedParameters, cancellationToken).ConfigureAwait(false);

        // `UnwrapResult<object>` would demand an envelope typed as object, which no server writes.
        return typeof(TResult) == typeof(object)
            ? (TResult?)_processor.UnwrapResult(method, format, result, key)
            : _processor.UnwrapResult<TResult>(method, format, result, key);
    }

    /// <summary>
    /// Calls a method that returns nothing in the format of <see cref="PayloadConnectorOptions.Format"/>, and waits for
    /// the server to confirm it ran.
    /// </summary>
    /// <param name="method">The method name.</param>
    /// <param name="parameters">The parameters. Only a plain call may pass <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token that cancels the call.</param>
    /// <returns>A task that completes when the server has answered.</returns>
    /// <exception cref="JsonRpcErrorException">The server answered with an error.</exception>
    /// <exception cref="InvalidOperationException">
    /// The call is encrypted and there is no key, or the parameters are <see langword="null"/> in a call that is not
    /// plain, or they could not be encoded.
    /// </exception>
    public Task InvokeAsync(string method, object? parameters, CancellationToken cancellationToken)
        => InvokeAsync(method, parameters, _format, cancellationToken);

    /// <summary>
    /// Calls a method that returns nothing in the given format, and waits for the server to confirm it ran.
    /// </summary>
    /// <param name="method">The method name.</param>
    /// <param name="parameters">The parameters. Only a plain call may pass <see langword="null"/>.</param>
    /// <param name="format">The format of this call.</param>
    /// <param name="cancellationToken">A token that cancels the call.</param>
    /// <returns>A task that completes when the server has answered.</returns>
    /// <exception cref="JsonRpcErrorException">The server answered with an error.</exception>
    /// <exception cref="InvalidOperationException">
    /// The call is encrypted and there is no key, or the parameters are <see langword="null"/> in a call that is not
    /// plain, or they could not be encoded.
    /// </exception>
    /// <remarks>
    /// The result is not opened, as <see cref="JsonRpcConnector"/> does not read the result of such a call, so a method
    /// that does return a value needs no registered result type here.
    /// </remarks>
    public Task InvokeAsync(string method, object? parameters, PayloadFormat format, CancellationToken cancellationToken)
        => _connector.InvokeAsync(method, Wrap(method, parameters, format, KeyFor(format)), cancellationToken);

    private byte[]? KeyFor(PayloadFormat format) => format == PayloadFormat.Encrypted ? _keyProvider?.Invoke() : null;

    private JsonElement Wrap(string method, object? parameters, PayloadFormat format, byte[]? key)
    {
        var sequence = format != PayloadFormat.Plain && _processor.Options.RequireFrame ? NextSequence() : 0;
        return _processor.WrapRequest(method, parameters, format, _codec, key, sequence);
    }

    private long NextSequence() => _sequenceGenerator?.Invoke() ?? Interlocked.Increment(ref _lastSequence);
}
