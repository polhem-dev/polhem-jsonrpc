using System.Net.Mime;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Polhem.JsonRpc.Server;

namespace Polhem.JsonRpc.AspNetCore;

/// <summary>
/// Answers JSON-RPC calls that arrive over HTTP POST. <c>MapJsonRpc</c> routes to it, and an MVC controller can
/// call it as well.
/// </summary>
public sealed class JsonRpcHttpHandler
{
    private const string JsonContentType = "application/json; charset=utf-8";

    private readonly JsonRpcDispatcher _dispatcher;
    private readonly JsonRpcHttpOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonRpcHttpHandler"/> class.
    /// </summary>
    /// <param name="dispatcher">The dispatcher that runs the calls.</param>
    /// <param name="options">The HTTP settings.</param>
    public JsonRpcHttpHandler(JsonRpcDispatcher dispatcher, JsonRpcHttpOptions options)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(options);
        _dispatcher = dispatcher;
        _options = options;
    }

    /// <summary>
    /// Answers one HTTP request.
    /// </summary>
    /// <param name="httpContext">The HTTP request.</param>
    /// <returns>A task that completes when the answer is written.</returns>
    /// <remarks>
    /// A body that is not <c>application/json</c> is answered with 415, one larger than
    /// <see cref="JsonRpcHttpOptions.MaxRequestBodySize"/> with 413, and a message that holds only notifications
    /// with 204 and no body. Every call this handler delivers is marked <see cref="JsonRpcTransportKind.Http"/>.
    /// </remarks>
    public async Task HandleAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var request = httpContext.Request;
        var aborted = httpContext.RequestAborted;

        if (!IsJson(request.ContentType))
        {
            await WriteErrorAsync(httpContext, StatusCodes.Status415UnsupportedMediaType, "Unsupported media type").ConfigureAwait(false);
            return;
        }

        if (request.ContentLength > _options.MaxRequestBodySize)
        {
            await WriteErrorAsync(httpContext, StatusCodes.Status413PayloadTooLarge, "Request too large").ConfigureAwait(false);
            return;
        }

        byte[]? body;
        try
        {
            body = await ReadBodyAsync(request.Body, _options.MaxRequestBodySize, aborted).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (aborted.IsCancellationRequested)
        {
            // The caller went away; there is no one to answer.
            return;
        }

        if (body is null)
        {
            await WriteErrorAsync(httpContext, StatusCodes.Status413PayloadTooLarge, "Request too large").ConfigureAwait(false);
            return;
        }

        var transport = new JsonRpcTransportInfo(
            JsonRpcTransportKind.Http,
            httpContext.RequestServices,
            ReadHeaders(request.Headers),
            httpContext.Connection.RemoteIpAddress?.ToString());

        JsonRpcDispatchResult result;
        try
        {
            result = await _dispatcher.DispatchMessageAsync(body, transport, aborted).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (aborted.IsCancellationRequested)
        {
            return;
        }

        var content = result.Serialize();
        if (content is null)
        {
            httpContext.Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }

        httpContext.Response.StatusCode = !result.IsBatch && _options.StatusCodeSelector is { } select
            ? select(result.Responses[0])
            : StatusCodes.Status200OK;
        httpContext.Response.ContentType = JsonContentType;
        await httpContext.Response.Body.WriteAsync(content, aborted).ConfigureAwait(false);
    }

    private static bool IsJson(string? contentType) =>
        MediaTypeHeaderValue.TryParse(contentType, out var mediaType)
        && mediaType.MediaType.Equals(MediaTypeNames.Application.Json, StringComparison.OrdinalIgnoreCase);

    private static async Task<byte[]?> ReadBodyAsync(Stream body, long limit, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > limit) { return null; }
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        return buffer.ToArray();
    }

    private static Dictionary<string, string> ReadHeaders(IHeaderDictionary headers)
    {
        var result = new Dictionary<string, string>(headers.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in headers)
        {
            result[name] = values.ToString();
        }
        return result;
    }

    private static async Task WriteErrorAsync(HttpContext httpContext, int statusCode, string message)
    {
        var response = JsonRpcResponse.Failure(JsonRpcId.Null, new JsonRpcError(JsonRpcErrorCodes.InvalidRequest, message));
        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = JsonContentType;
        await httpContext.Response.Body.WriteAsync(JsonRpcSerializer.SerializeResponse(response), httpContext.RequestAborted).ConfigureAwait(false);
    }
}
