using System.Net.Mime;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Polhem.JsonRpc.Server;

namespace Polhem.JsonRpc.AspNetCore;

/// <summary>
/// Answers JSON-RPC calls that arrive over HTTP POST. <see cref="JsonRpcEndpointRouteBuilderExtensions.MapJsonRpc"/> routes to it, and an MVC controller can
/// call it as well.
/// </summary>
public sealed class JsonRpcHttpHandler
{
    private const string JsonContentType = "application/json; charset=utf-8";

    private readonly JsonRpcDispatcher _dispatcher;
    private readonly JsonRpcHttpOptions _options;

    private const int InitialBufferBytes = 64 * 1024;

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

        ReadOnlyMemory<byte>? body;
        try
        {
            body = await ReadBodyAsync(request.Body, request.ContentLength, _options.MaxRequestBodySize, aborted).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (aborted.IsCancellationRequested)
        {
            // The caller went away; there is no one to answer.
            return;
        }

        if (body is not { } message)
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
            result = await _dispatcher.DispatchMessageAsync(message, transport, aborted).ConfigureAwait(false);
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

    // The body is read once and handed on without the copy ToArray would make. Content-Length is what the caller claims,
    // before it has sent anything: it only sizes the first buffer up to InitialBufferBytes, so memory grows with the
    // bytes that actually arrive, and the count is checked against the limit as it reads.
    private static async Task<ReadOnlyMemory<byte>?> ReadBodyAsync(Stream body, long? length, long limit, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream(length is > 0 ? (int)Math.Min(length.Value, InitialBufferBytes) : 0);
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > limit) { return null; }
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        return new ReadOnlyMemory<byte>(buffer.GetBuffer(), 0, (int)buffer.Length);
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
