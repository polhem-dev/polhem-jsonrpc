using System.ComponentModel;
using System.Text;
using System.Text.Json;

namespace Polhem.JsonRpc.UnitTests;

public class SerializerTests
{
    [Fact]
    [DisplayName("Serializer: a notification is written without an id member")]
    public void SerializeRequest_Notification_OmitsId()
    {
        var json = Encoding.UTF8.GetString(JsonRpcSerializer.SerializeRequest(new JsonRpcRequest("a.b")));

        Assert.Equal("""{"jsonrpc":"2.0","method":"a.b"}""", json);
    }

    [Theory]
    [DisplayName("Serializer: string, number and null ids round-trip")]
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

    [Fact]
    [DisplayName("Serializer: a response always has an id member, null when it has none")]
    public void SerializeResponse_NoId_WritesNullId()
    {
        var json = Encoding.UTF8.GetString(JsonRpcSerializer.SerializeResponse(JsonRpcResponse.Success(JsonRpcId.None, null)));

        Assert.Equal("""{"jsonrpc":"2.0","result":null,"id":null}""", json);
    }

    [Fact]
    [DisplayName("Serializer: additional members are written after jsonrpc, but never in place of a standard member")]
    public void SerializeResponse_AdditionalMembers_SkipsStandardNames()
    {
        var response = JsonRpcResponse.Success(1, JsonSerializer.SerializeToElement(5));
        response.AdditionalMembers = new Dictionary<string, JsonElement>
        {
            ["method"] = JsonSerializer.SerializeToElement("a.b"),
            ["result"] = JsonSerializer.SerializeToElement(99),
        };

        var json = Encoding.UTF8.GetString(JsonRpcSerializer.SerializeResponse(response));

        Assert.Equal("""{"jsonrpc":"2.0","method":"a.b","result":5,"id":1}""", json);
    }

    [Fact]
    [DisplayName("Serializer: OmitNullId leaves a null id out, as the Polhem wire format does")]
    public void SerializeResponse_OmitNullId_LeavesNullIdOut()
    {
        var response = JsonRpcResponse.Failure(JsonRpcId.Null, new JsonRpcError(-32601, "Method not found."));
        response.AdditionalMembers = new Dictionary<string, JsonElement> { ["method"] = JsonSerializer.SerializeToElement("A.B") };

        var json = Encoding.UTF8.GetString(JsonRpcSerializer.SerializeResponse(response, new JsonRpcWriteOptions { OmitNullId = true }));

        Assert.Equal("""{"jsonrpc":"2.0","method":"A.B","error":{"code":-32601,"message":"Method not found."}}""", json);
    }

    [Fact]
    [DisplayName("Serializer: OmitNullId still writes an id that is a string or a number")]
    public void SerializeResponse_OmitNullIdWithId_WritesId()
    {
        var json = Encoding.UTF8.GetString(JsonRpcSerializer.SerializeResponse(
            JsonRpcResponse.Success("x", null), new JsonRpcWriteOptions { OmitNullId = true }));

        Assert.Equal("""{"jsonrpc":"2.0","result":null,"id":"x"}""", json);
    }

    [Fact]
    [DisplayName("Serializer: unknown response members are kept as additional members when read")]
    public void ReadResponse_UnknownMember_IsKept()
    {
        var response = JsonRpcSerializer.ReadResponses(Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","result":1,"id":1,"method":"a.b"}"""))[0];

        Assert.Equal("a.b", response.AdditionalMembers!["method"].GetString());
    }

    [Fact]
    [DisplayName("Serializer: an error response with data is read completely")]
    public void ReadResponse_Error_IsRead()
    {
        var response = JsonRpcSerializer.ReadResponses(Encoding.UTF8.GetBytes(
            """{"jsonrpc":"2.0","error":{"code":-32602,"message":"Invalid params","data":{"field":"a"}},"id":"x"}"""))[0];

        Assert.False(response.IsSuccess);
        Assert.Equal(-32602, response.Error!.Code);
        Assert.Equal("a", response.Error.Data!.Value.GetProperty("field").GetString());
        Assert.Equal(JsonRpcId.FromString("x"), response.Id);
    }

    [Fact]
    [DisplayName("Serializer: a response that is not an object is rejected")]
    public void ReadResponses_NotAnObject_Throws()
    {
        Assert.ThrowsAny<JsonException>(() => JsonRpcSerializer.ReadResponses(Encoding.UTF8.GetBytes("[1]")));
    }
}
