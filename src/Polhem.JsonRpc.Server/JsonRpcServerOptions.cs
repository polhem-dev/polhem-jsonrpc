using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Polhem.JsonRpc.Server;

/// <summary>
/// Settings for <see cref="JsonRpcDispatcher"/>.
/// </summary>
public sealed class JsonRpcServerOptions
{
    internal const DynamicallyAccessedMemberTypes TargetMembers =
        DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor;

    private readonly Dictionary<string, Type> _targets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the options that bind <c>params</c> and serialize results. The default uses
    /// <see cref="JsonSerializerDefaults.Web"/>: camelCase names, case-insensitive reading.
    /// </summary>
    public JsonSerializerOptions SerializerOptions { get; set; } = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Gets the registered targets, by name.
    /// </summary>
    public IReadOnlyDictionary<string, Type> Targets => _targets;

    /// <summary>
    /// Gets or sets the method resolver. When <c>null</c>, methods are resolved by convention: <c>target.action</c>
    /// names the target registered as <c>target</c> and its method <c>action</c>.
    /// </summary>
    public IJsonRpcMethodResolver? MethodResolver { get; set; }

    /// <summary>
    /// Gets or sets which methods may be called. The default admits methods marked with
    /// <see cref="JsonRpcMethodAttribute"/>. It is used by the default resolver only.
    /// </summary>
    public IJsonRpcMethodPolicy MethodPolicy { get; set; } = new JsonRpcMethodAttributePolicy();

    /// <summary>
    /// Gets or sets the factory that creates targets. When <c>null</c>, a target is taken from
    /// <see cref="JsonRpcTransportInfo.Services"/> when it is registered there, and created with its parameterless
    /// constructor otherwise.
    /// </summary>
    public IJsonRpcTargetFactory? TargetFactory { get; set; }

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
    /// By default an exception is answered with <see cref="InternalErrorCode"/> and a fixed message; its own message
    /// is not sent unless <see cref="IncludeExceptionDetails"/> is set.
    /// </remarks>
    public Func<Exception, JsonRpcRequestContext, JsonRpcError?>? ExceptionMapper { get; set; }

    /// <summary>
    /// Gets or sets the code of the error that answers an unexpected exception. The default is
    /// <see cref="JsonRpcErrorCodes.InternalError"/>; a host can choose another to keep an older wire format.
    /// </summary>
    public int InternalErrorCode { get; set; } = JsonRpcErrorCodes.InternalError;

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

    /// <summary>
    /// Registers a target type under a name, so that <c>name.action</c> calls the method <c>action</c> on it.
    /// </summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="name">The target name: letters, digits, underscores and hyphens.</param>
    /// <returns>These options.</returns>
    public JsonRpcServerOptions AddTarget<[DynamicallyAccessedMembers(TargetMembers)] T>(string name) where T : class
        => AddTarget(name, typeof(T));

    /// <summary>
    /// Registers a target type under a name, so that <c>name.action</c> calls the method <c>action</c> on it.
    /// </summary>
    /// <param name="name">The target name: letters, digits, underscores and hyphens.</param>
    /// <param name="targetType">The target type.</param>
    /// <returns>These options.</returns>
    public JsonRpcServerOptions AddTarget(string name, [DynamicallyAccessedMembers(TargetMembers)] Type targetType)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        if (!ConventionMethodResolver.IsValidName(name, allowHyphen: true))
        {
            throw new ArgumentException("A target name holds 1 to 64 letters, digits, underscores and hyphens.", nameof(name));
        }
        _targets[name] = targetType;
        return this;
    }
}
