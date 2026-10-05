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
    /// Gets or sets a value indicating whether the dispatcher starts while application code compiled against
    /// <c>Polhem.JsonRpc.Server</c> 1.0 is loaded. The default is <see langword="false"/>: the constructor of
    /// <see cref="JsonRpcDispatcher"/> throws <see cref="InvalidOperationException"/> naming that code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 1.1 renumbered <see cref="JsonRpcTransportKind"/>, and the value is compiled into the code that uses it. Code
    /// compiled against 1.0 that reads the kind takes every HTTP call for an in-process one, and code that sets it marks
    /// an HTTP call as in-process. Set this only when the code named in the exception neither reads nor sets the
    /// transport kind. The check sees the assemblies loaded when the dispatcher is created.
    /// </para>
    /// <para>
    /// IMPORTANT: this never covers the Polhem.JsonRpc packages themselves. <c>Polhem.JsonRpc.AspNetCore</c> 1.0,
    /// brought in by upgrading only another package, sets the kind of every HTTP call to the value 1.1 reads as
    /// in-process, so the dispatcher refuses to start beside it whatever this is set to
    /// (<c>CompiledVersionGuardTests.Dispatcher_StalePackageAllowed_Throws</c>).
    /// </para>
    /// </remarks>
    public bool AllowCodeCompiledAgainst10 { get; set; }
}
