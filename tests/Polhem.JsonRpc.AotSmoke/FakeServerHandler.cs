using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Polhem.JsonRpc.AotSmoke;

/// <summary>
/// Answers <c>math.add</c> with the sum and <c>math.fail</c> with error -32001, for single requests and batches.
/// </summary>
internal sealed class FakeServerHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
        var parsed = JsonRpcSerializer.ReadRequests(body);
        var responses = parsed.Entries
            .Where(entry => entry.IsValid && !entry.Request.IsNotification)
            .Select(entry => Answer(entry.Request!))
            .ToList();

        if (responses.Count == 0) { return new HttpResponseMessage(HttpStatusCode.NoContent); }
        var json = parsed.IsBatch ? JsonRpcSerializer.SerializeResponses(responses) : JsonRpcSerializer.SerializeResponse(responses[0]);
        var content = new ByteArrayContent(json);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static JsonRpcResponse Answer(JsonRpcRequest request)
    {
        if (request.Method == "math.fail")
        {
            return JsonRpcResponse.Failure(request.Id, new JsonRpcError(-32001, "Failed on purpose"));
        }
        var parameters = request.Params!.Value;
        var sum = parameters.GetProperty("a").GetInt32() + parameters.GetProperty("b").GetInt32();
        return JsonRpcResponse.Success(request.Id, JsonSerializer.SerializeToElement(sum, SmokeJsonContext.Default.Int32));
    }
}
