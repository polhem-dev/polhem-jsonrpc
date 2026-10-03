using System.Net;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text.Json;

namespace Polhem.JsonRpc.Client;

/// <summary>
/// Sends JSON-RPC requests as HTTP POST requests.
/// </summary>
/// <remarks>
/// A JSON-RPC answer is read whatever the HTTP status is, because some servers answer errors with 4xx or 5xx and an
/// error object in the body. A body that is not a JSON-RPC answer raises <see cref="HttpRequestException"/> when the
/// status is not a success, and <see cref="JsonException"/> otherwise.
/// </remarks>
public sealed class HttpTransport : IJsonRpcTransport
{
    private readonly HttpClient _httpClient;
    private readonly Uri? _endpoint;

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpTransport"/> class.
    /// </summary>
    /// <param name="httpClient">The client that sends the requests. Add headers with its handlers.</param>
    /// <param name="endpoint">The endpoint, absolute or relative to <see cref="HttpClient.BaseAddress"/>; <c>null</c> posts to the base address itself.</param>
    public HttpTransport(HttpClient httpClient, Uri? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _endpoint = endpoint;
    }

    /// <inheritdoc/>
    public async Task<JsonRpcResponse?> SendAsync(JsonRpcRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var responses = await PostAsync(JsonRpcSerializer.SerializeRequest(request), cancellationToken).ConfigureAwait(false);
        if (request.IsNotification || responses.Count == 0) { return null; }
        return responses.Count == 1
            ? responses[0]
            : throw new JsonException("The server answered a single request with more than one response.");
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<JsonRpcResponse>> SendBatchAsync(IReadOnlyList<JsonRpcRequest> requests, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        return PostAsync(JsonRpcSerializer.SerializeRequests(requests), cancellationToken);
    }

    private async Task<IReadOnlyList<JsonRpcResponse>> PostAsync(byte[] body, CancellationToken cancellationToken)
    {
        using var content = new ByteArrayContent(body);
        // A new value for each request: a handler may change the header value in place, and a shared one would carry the
        // change into every later request of the process.
        content.Headers.ContentType = new MediaTypeHeaderValue(MediaTypeNames.Application.Json) { CharSet = "utf-8" };
        using var message = new HttpRequestMessage(HttpMethod.Post, _endpoint) { Content = content };
        using var response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NoContent || bytes.Length == 0)
        {
            response.EnsureSuccessStatusCode();
            return [];
        }

        try
        {
            return JsonRpcSerializer.ReadResponses(bytes);
        }
        catch (JsonException) when (!response.IsSuccessStatusCode)
        {
            response.EnsureSuccessStatusCode();
            throw;
        }
    }
}
