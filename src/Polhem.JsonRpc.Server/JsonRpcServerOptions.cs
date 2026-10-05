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
    /// <remarks>
    /// The dispatcher keeps the factory for its whole life and calls it from every request at once, so it must be
    /// thread-safe and must not depend on a scoped service. It reaches the services of each call through
    /// <see cref="JsonRpcRequestContext.Services"/>.
    /// </remarks>
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
    /// default handling. It sees every exception except <see cref="JsonRpcErrorException"/> and the
    /// <see cref="OperationCanceledException"/> of a call its caller cancelled, so it is also the place to log them.
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
    /// <remarks>
    /// Zero or a negative value refuses every batch; a single request is not affected.
    /// </remarks>
    public int MaxBatchSize { get; set; } = 100;

    /// <summary>
    /// Has no effect since 1.1.1: the constructor of <see cref="JsonRpcDispatcher"/> throws
    /// <see cref="InvalidOperationException"/> while code compiled against <c>Polhem.JsonRpc.Server</c> 1.0 is loaded,
    /// whatever this is set to.
    /// </summary>
    /// <remarks>
    /// 1.1 renumbered <see cref="JsonRpcTransportKind"/>, and the value is compiled into the code that uses it, so code
    /// compiled against 1.0 that reads or sets the kind takes HTTP calls and in-process calls for each other. In 1.1.0
    /// this let such code run; it let <c>Polhem.JsonRpc.AspNetCore</c> 1.0 mark every HTTP call as in-process, and the
    /// Polhem framework 1.2.0 take every HTTP call for an in-process one, both of which bypass an application's access
    /// checks for in-process calls. Nothing can tell from outside whether code reads or sets the kind, so the check
    /// no longer has an exception (<c>CompiledVersionGuardTests.Dispatcher_StaleAssemblyEvenIfAllowed_Throws</c>). The
    /// check sees the assemblies loaded when the dispatcher is created.
    /// </remarks>
    [Obsolete("Has no effect since 1.1.1: recompile code built against Polhem.JsonRpc.Server 1.0.")]
    public bool AllowCodeCompiledAgainst10 { get; set; }
}
