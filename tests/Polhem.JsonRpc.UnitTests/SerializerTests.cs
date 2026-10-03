using System.Text;
using System.Text.Json;

namespace Polhem.JsonRpc.UnitTests;

public class SerializerTests
{
    [Fact(DisplayName = "Serializer: a notification is written without an id member")]
    public void SerializeRequest_Notification_OmitsId()
    {
        var json = Encoding.UTF8.GetString(JsonRpcSerializer.SerializeRequest(new JsonRpcRequest("a.b")));

        Assert.Equal("""{"jsonrpc":"2.0","method":"a.b"}""", json);
    }

    [Fact(DisplayName = "Serializer: a request and a response that start with a UTF-8 byte order mark are read")]
    public void Read_ByteOrderMark_IsIgnored()
    {
        byte[] bom = [0xEF, 0xBB, 0xBF];

        var parsed = JsonRpcSerializer.ReadRequests((byte[])[.. bom, .. Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","method":"a.b","id":1}""")]);
        var responses = JsonRpcSerializer.ReadResponses((byte[])[.. bom, .. Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","result":7,"id":1}""")]);

        Assert.Equal("a.b", Assert.Single(parsed.Entries).Request!.Method);
        Assert.Equal(7, Assert.Single(responses).Result!.Value.GetInt32());
    }

    [Theory(DisplayName = "Serializer: a batch larger than the limit is read as a single invalid request, and one within it as a batch")]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void ReadRequests_MaxBatchSize_RefusesLargerBatch(int maxBatchSize, bool isBatch)
    {
        var json = Encoding.UTF8.GetBytes("""[{"jsonrpc":"2.0","method":"a.b","id":1},{"jsonrpc":"2.0","method":"a.b","id":2},{"jsonrpc":"2.0","method":"a.b","id":3}]""");

        var parsed = JsonRpcSerializer.ReadRequests(json, maxBatchSize);

        if (isBatch)
        {
            Assert.True(parsed.IsBatch);
            Assert.Equal(3, parsed.Entries.Count);
        }
        else
        {
            Assert.False(parsed.IsBatch);
            var entry = Assert.Single(parsed.Entries);
            Assert.False(entry.IsValid);
            Assert.Equal(JsonRpcErrorCodes.InvalidRequest, entry.Error!.Code);
        }
    }

    [Theory(DisplayName = "Serializer: string, number and null ids round-trip")]
    [InlineData("\"abc\"")]
    [InlineData("42")]
    [InlineData("null")]
    public void ReadRequest_Id_RoundTrips(string id)
    {
        var parsed = JsonRpcSerializer.ReadRequests(Encoding.UTF8.GetBytes($$"""{"jsonrpc":"2.0","method":"a.b","id":{{id}}}"""));
        var request = parsed.Entries[0].Request!;

        var json = Encoding.UTF8.GetString(JsonRpcSerializer.SerializeRequest(request));

        Assert.EndsWith($"\"id\":{id}}}", json);
    }

    [Fact(DisplayName = "Serializer: a response always has an id member, null when it has none")]
    public void SerializeResponse_NoId_WritesNullId()
    {
        var json = Encoding.UTF8.GetString(JsonRpcSerializer.SerializeResponse(JsonRpcResponse.Success(JsonRpcId.None, null)));

        Assert.Equal("""{"jsonrpc":"2.0","result":null,"id":null}""", json);
    }

    [Fact(DisplayName = "Serializer: unknown response members are ignored when read")]
    public void ReadResponse_UnknownMember_IsIgnored()
    {
        var response = JsonRpcSerializer.ReadResponses(Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","result":1,"id":1,"method":"a.b"}"""))[0];

        Assert.True(response.IsSuccess);
        Assert.Equal(1, response.Result!.Value.GetInt32());
        Assert.Equal(JsonRpcId.FromNumber(1), response.Id);
    }

    [Fact(DisplayName = "Serializer: an error response with data is read completely")]
    public void ReadResponse_Error_IsRead()
    {
        var response = JsonRpcSerializer.ReadResponses(Encoding.UTF8.GetBytes(
            """{"jsonrpc":"2.0","error":{"code":-32602,"message":"Invalid params","data":{"field":"a"}},"id":"x"}"""))[0];

        Assert.False(response.IsSuccess);
        Assert.Equal(-32602, response.Error!.Code);
        Assert.Equal("a", response.Error.Data!.Value.GetProperty("field").GetString());
        Assert.Equal(JsonRpcId.FromString("x"), response.Id);
    }

    [Fact(DisplayName = "Serializer: a response that is not an object is rejected")]
    public void ReadResponses_NotAnObject_Throws()
    {
        Assert.ThrowsAny<JsonException>(() => JsonRpcSerializer.ReadResponses(Encoding.UTF8.GetBytes("[1]")));
    }
}
