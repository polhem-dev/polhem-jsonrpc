namespace Polhem.JsonRpc.Server;

/// <summary>
/// Runs the rest of the pipeline: the next filter, or binding and invoking the method.
/// </summary>
/// <param name="context">The call.</param>
/// <returns>A task that completes when the rest of the pipeline has run.</returns>
public delegate ValueTask JsonRpcFilterDelegate(JsonRpcRequestContext context);
