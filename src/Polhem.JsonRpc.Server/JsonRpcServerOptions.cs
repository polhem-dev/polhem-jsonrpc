using System.Text.Json;

namespace Polhem.JsonRpc.Server;

/// <summary>
/// Settings for <see cref="JsonRpcDispatcher"/>.
/// </summary>
public sealed class JsonRpcServerOptions
{
    /// <summary>
    /// Gets or sets the options that bind <c>params</c> and serialize results. The default uses
    /// <see cref="JsonSerializerDefaults.Web"/>: camelCase names, case-insensitive reading.
    /// </summary>
    public JsonSerializerOptions SerializerOptions { get; set; } = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Gets or sets the factory that creates the object for the ProgId of a method name. The dispatcher requires
    /// one.
    /// </summary>
    public IJsonRpcObjectFactory? ObjectFactory { get; set; }

    /// <summary>
    /// Gets or sets which methods may be called. The default is <see cref="JsonRpcNamingConventionPolicy"/>.
    /// </summary>
    public IJsonRpcMethodPolicy MethodPolicy { get; set; } = new JsonRpcNamingConventionPolicy();

    /// <summary>
    /// Gets or sets the parameter binder. When <c>null</c>, <c>params</c> must be an object, which is deserialized
    /// into the method's parameter with <see cref="SerializerOptions"/>.
    /// </summary>
    public IJsonRpcParameterBinder? ParameterBinder { get; set; }

    /// <summary>
    /// Gets the filters, run in order around every call.
    /// </summary>
    public IList<IJsonRpcFilter> Filters { get; } = [];

    /// <summary>
    /// Gets or sets a function that turns an exception into an error, or returns <c>null</c> to leave it to the
    /// default handling. It sees every exception except <see cref="JsonRpcErrorException"/>, so it is also the
    /// place to log them.
    /// </summary>
    /// <remarks>
    /// By default an exception is answered with <see cref="JsonRpcErrorCodes.InternalError"/> and a fixed message; its own message
    /// is not sent unless <see cref="IncludeExceptionDetails"/> is set.
    /// </remarks>
    public Func<Exception, JsonRpcRequestContext, JsonRpcError?>? ExceptionMapper { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the message of an unexpected exception is sent in the error's
    /// <c>data</c>. Turn it on in development only: exception messages can reveal internal details.
    /// </summary>
    public bool IncludeExceptionDetails { get; set; }

    /// <summary>
    /// Gets or sets the largest number of requests a batch may hold. A larger batch is answered with a single
    /// <see cref="JsonRpcErrorCodes.InvalidRequest"/> error. The default is 100.
    /// </summary>
    public int MaxBatchSize { get; set; } = 100;
}
