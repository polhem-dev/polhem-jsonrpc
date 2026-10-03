using System.Buffers;
using System.Text.Json;

namespace Polhem.JsonRpc;

/// <summary>
/// Reads and writes JSON-RPC 2.0 messages.
/// </summary>
/// <remarks>
/// The envelope is read and written by hand with <see cref="JsonDocument"/> and <see cref="Utf8JsonWriter"/>, not
/// with <see cref="JsonSerializer"/>, so it needs no reflection and works under trimming and Native AOT. Reading by
/// hand also tells an invalid request (-32600) apart from invalid JSON (-32700), which a deserializer reports
/// alike. The <c>params</c>, <c>result</c> and <c>error.data</c> members are kept as <see cref="JsonElement"/>
/// values; turning them into objects is the caller's concern.
/// </remarks>
public static class JsonRpcSerializer
{
    private const string Version = "2.0";

    private const string JsonRpcMember = "jsonrpc";
    private const string ResultMember = "result";
    private const string ErrorMember = "error";
    private const string IdMember = "id";

    // Error messages are protocol text sent to the caller, so they are fixed and never echo the input.
    private const string ParseErrorMessage = "Parse error";
    private const string InvalidRequestMessage = "Invalid Request";

    /// <summary>
    /// Reads an incoming message: a single request, a batch, or something the specification answers with an error.
    /// </summary>
    /// <param name="utf8Json">The message as UTF-8 JSON.</param>
    /// <returns>
    /// The entries of the message. Invalid JSON and an empty array each give a single invalid entry, answered with
    /// a single error object.
    /// </returns>
    public static JsonRpcRequestParseResult ReadRequests(ReadOnlyMemory<byte> utf8Json) => ReadRequests(utf8Json, int.MaxValue);

    /// <summary>
    /// Reads an incoming message like <see cref="ReadRequests(ReadOnlyMemory{byte})"/>, and refuses a batch larger than
    /// <paramref name="maxBatchSize"/> before reading any of its entries.
    /// </summary>
    /// <param name="utf8Json">The message as UTF-8 JSON.</param>
    /// <param name="maxBatchSize">The largest number of requests a batch may hold.</param>
    /// <returns>
    /// The entries of the message. Invalid JSON, an empty array and a batch larger than <paramref name="maxBatchSize"/>
    /// each give a single invalid entry, answered with a single error object.
    /// </returns>
    public static JsonRpcRequestParseResult ReadRequests(ReadOnlyMemory<byte> utf8Json, int maxBatchSize)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(WithoutByteOrderMark(utf8Json));
        }
        catch (JsonException)
        {
            return Single(JsonRpcParsedRequest.Invalid(new JsonRpcError(JsonRpcErrorCodes.ParseError, ParseErrorMessage), JsonRpcId.Null));
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
            {
                return Single(ReadRequest(root));
            }

            // The length is known from the parsed document, so an oversized batch is refused before a request object
            // is built for each of its entries.
            var length = root.GetArrayLength();
            if (length == 0 || length > maxBatchSize)
            {
                return Single(InvalidRequest(JsonRpcId.Null));
            }

            var entries = new List<JsonRpcParsedRequest>(length);
            foreach (var element in root.EnumerateArray())
            {
                entries.Add(ReadRequest(element));
            }
            return new JsonRpcRequestParseResult(isBatch: true, entries);
        }
    }

    /// <summary>
    /// Reads one request object.
    /// </summary>
    /// <param name="element">The element that should hold a request object.</param>
    /// <returns>The request, or the error that answers it.</returns>
    public static JsonRpcParsedRequest ReadRequest(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return InvalidRequest(JsonRpcId.Null);
        }

        // The id is read first, so that an otherwise invalid request is still answered with its own id.
        var id = JsonRpcId.None;
        if (element.TryGetProperty(IdMember, out var idElement) && !TryReadId(idElement, out id))
        {
            return InvalidRequest(JsonRpcId.Null);
        }

        if (!element.TryGetProperty(JsonRpcMember, out var version)
            || version.ValueKind != JsonValueKind.String
            || !version.ValueEquals(Version))
        {
            return InvalidRequest(id);
        }

        if (!element.TryGetProperty("method", out var method) || method.ValueKind != JsonValueKind.String)
        {
            return InvalidRequest(id);
        }

        JsonElement? parameters = null;
        if (element.TryGetProperty("params", out var paramsElement))
        {
            if (paramsElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
            {
                return InvalidRequest(id);
            }
            parameters = paramsElement.Clone();
        }

        return JsonRpcParsedRequest.Valid(new JsonRpcRequest(method.GetString()!, parameters, id));
    }

    /// <summary>
    /// Reads the response to a request or a batch.
    /// </summary>
    /// <param name="utf8Json">The response as UTF-8 JSON: an object, or an array for a batch.</param>
    /// <returns>The responses; a single object gives a list of one.</returns>
    /// <exception cref="JsonException">The content is not a JSON-RPC response.</exception>
    public static IReadOnlyList<JsonRpcResponse> ReadResponses(ReadOnlyMemory<byte> utf8Json)
    {
        using var document = JsonDocument.Parse(WithoutByteOrderMark(utf8Json));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array)
        {
            return [ReadResponse(root)];
        }

        var responses = new List<JsonRpcResponse>(root.GetArrayLength());
        foreach (var element in root.EnumerateArray())
        {
            responses.Add(ReadResponse(element));
        }
        return responses;
    }

    /// <summary>
    /// Reads one response object.
    /// </summary>
    /// <param name="element">The response object.</param>
    /// <returns>The response.</returns>
    /// <exception cref="JsonException">The element is not a JSON-RPC response.</exception>
    /// <remarks>
    /// Reading is lenient where a server that predates this library differs from the specification: a missing
    /// <c>id</c> reads as <see cref="JsonRpcId.Null"/>, a response with neither <c>result</c> nor <c>error</c> reads
    /// as a success with no result, and members other than <c>jsonrpc</c>, <c>result</c>, <c>error</c> and <c>id</c> are ignored.
    /// </remarks>
    public static JsonRpcResponse ReadResponse(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("A JSON-RPC response must be an object.");
        }

        var id = JsonRpcId.Null;
        if (element.TryGetProperty(IdMember, out var idElement) && !TryReadId(idElement, out id))
        {
            throw new JsonException("The id of a JSON-RPC response must be a string, an integer or null.");
        }

        JsonRpcResponse response;
        if (element.TryGetProperty(ErrorMember, out var errorElement) && errorElement.ValueKind != JsonValueKind.Null)
        {
            response = JsonRpcResponse.Failure(id, ReadError(errorElement));
        }
        else
        {
            JsonElement? result = element.TryGetProperty(ResultMember, out var resultElement) ? resultElement.Clone() : null;
            response = JsonRpcResponse.Success(id, result);
        }
        return response;
    }

    /// <summary>
    /// Serializes a request.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>The request as UTF-8 JSON.</returns>
    public static byte[] SerializeRequest(JsonRpcRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Serialize(writer => WriteRequest(writer, request));
    }

    /// <summary>
    /// Serializes a batch of requests.
    /// </summary>
    /// <param name="requests">The requests.</param>
    /// <returns>The batch as a UTF-8 JSON array.</returns>
    public static byte[] SerializeRequests(IEnumerable<JsonRpcRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        return Serialize(writer =>
        {
            writer.WriteStartArray();
            foreach (var request in requests)
            {
                WriteRequest(writer, request);
            }
            writer.WriteEndArray();
        });
    }

    /// <summary>
    /// Writes a request.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="request">The request.</param>
    public static void WriteRequest(Utf8JsonWriter writer, JsonRpcRequest request)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(request);

        writer.WriteStartObject();
        writer.WriteString(JsonRpcMember, Version);
        writer.WriteString("method", request.Method);
        if (request.Params is { } parameters)
        {
            writer.WritePropertyName("params");
            parameters.WriteTo(writer);
        }
        if (!request.Id.IsNone)
        {
            writer.WritePropertyName(IdMember);
            WriteId(writer, request.Id);
        }
        writer.WriteEndObject();
    }

    /// <summary>
    /// Serializes a response.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <returns>The response as UTF-8 JSON.</returns>
    public static byte[] SerializeResponse(JsonRpcResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return Serialize(writer => WriteResponse(writer, response));
    }

    /// <summary>
    /// Serializes the responses to a batch.
    /// </summary>
    /// <param name="responses">The responses.</param>
    /// <returns>The responses as a UTF-8 JSON array.</returns>
    public static byte[] SerializeResponses(IEnumerable<JsonRpcResponse> responses)
    {
        ArgumentNullException.ThrowIfNull(responses);
        return Serialize(writer =>
        {
            writer.WriteStartArray();
            foreach (var response in responses)
            {
                WriteResponse(writer, response);
            }
            writer.WriteEndArray();
        });
    }

    /// <summary>
    /// Writes a response.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="response">The response.</param>
    /// <remarks>
    /// Members are written in the order <c>jsonrpc</c>, <c>result</c> or <c>error</c>, <c>id</c>. The <c>id</c> member is
    /// always written, as <c>null</c> when the response has no id.
    /// </remarks>
    public static void WriteResponse(Utf8JsonWriter writer, JsonRpcResponse response)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(response);

        writer.WriteStartObject();
        writer.WriteString(JsonRpcMember, Version);
        if (response.Error is { } error)
        {
            writer.WritePropertyName(ErrorMember);
            writer.WriteStartObject();
            writer.WriteNumber("code", error.Code);
            writer.WriteString("message", error.Message);
            if (error.Data is { } data)
            {
                writer.WritePropertyName("data");
                data.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        else
        {
            writer.WritePropertyName(ResultMember);
            if (response.Result is { } result)
            {
                result.WriteTo(writer);
            }
            else
            {
                writer.WriteNullValue();
            }
        }

        writer.WritePropertyName(IdMember);
        WriteId(writer, response.Id);
        writer.WriteEndObject();
    }

    private static JsonRpcError ReadError(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.Number
            || !code.TryGetInt32(out var codeValue))
        {
            throw new JsonException("The error of a JSON-RPC response must be an object with an integer code.");
        }

        var message = element.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String
            ? messageElement.GetString()!
            : string.Empty;
        JsonElement? data = element.TryGetProperty("data", out var dataElement) ? dataElement.Clone() : null;
        return new JsonRpcError(codeValue, message, data);
    }

    private static bool TryReadId(JsonElement element, out JsonRpcId id)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                id = JsonRpcId.FromString(element.GetString()!);
                return true;
            case JsonValueKind.Number when element.TryGetInt64(out var number):
                id = JsonRpcId.FromNumber(number);
                return true;
            case JsonValueKind.Null:
                id = JsonRpcId.Null;
                return true;
            default:
                id = JsonRpcId.None;
                return false;
        }
    }

    private static void WriteId(Utf8JsonWriter writer, JsonRpcId id)
    {
        switch (id.Kind)
        {
            case JsonRpcIdKind.String:
                writer.WriteStringValue(id.StringValue);
                break;
            case JsonRpcIdKind.Number:
                writer.WriteNumberValue(id.NumberValue);
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }

    private static JsonRpcRequestParseResult Single(JsonRpcParsedRequest entry) => new(isBatch: false, [entry]);

    private static JsonRpcParsedRequest InvalidRequest(JsonRpcId id) =>
        JsonRpcParsedRequest.Invalid(new JsonRpcError(JsonRpcErrorCodes.InvalidRequest, InvalidRequestMessage), id);

    private static byte[] Serialize(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            write(writer);
        }
        return buffer.WrittenSpan.ToArray();
    }

    // RFC 8259 lets a parser ignore a UTF-8 byte order mark, and some HTTP clients send one; JsonDocument does not
    // skip it.
    private static ReadOnlyMemory<byte> WithoutByteOrderMark(ReadOnlyMemory<byte> utf8Json)
        => utf8Json.Span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? utf8Json[3..] : utf8Json;
}
