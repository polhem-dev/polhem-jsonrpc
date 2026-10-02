using System.Text.Json;

namespace Polhem.JsonRpc.Client;

/// <summary>
/// Settings for <see cref="JsonRpcConnector"/>.
/// </summary>
public sealed class JsonRpcClientOptions
{
    /// <summary>
    /// Gets or sets the options that serialize parameters and read results. The default uses
    /// <see cref="JsonSerializerDefaults.Web"/>: camelCase names, case-insensitive reading.
    /// </summary>
    /// <remarks>
    /// IMPORTANT: under Native AOT, and on iOS where reflection-based serialization is off, set options whose
    /// <see cref="JsonSerializerOptions.TypeInfoResolver"/> is a source-generated <c>JsonSerializerContext</c> that
    /// covers every parameter and result type. Otherwise the call fails with <see cref="InvalidOperationException"/>.
    /// </remarks>
    public JsonSerializerOptions SerializerOptions { get; set; } = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Gets or sets a function that creates request ids, or <c>null</c> to number requests 1, 2, 3 and so on.
    /// </summary>
    public Func<JsonRpcId>? IdGenerator { get; set; }

    /// <summary>
    /// Gets or sets how long a call may take before it is cancelled, or <c>null</c> to leave it to the transport.
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Gets or sets a function that turns an error response into the exception to throw, or returns <c>null</c> to
    /// throw <see cref="JsonRpcErrorException"/>.
    /// </summary>
    public Func<JsonRpcError, Exception?>? ErrorMapper { get; set; }

    /// <summary>
    /// Gets the interceptors, run in order on every request and response.
    /// </summary>
    public IList<IJsonRpcClientInterceptor> Interceptors { get; } = [];
}
