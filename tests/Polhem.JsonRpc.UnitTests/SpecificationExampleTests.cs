using System.Text.Json;
using System.Text.Json.Nodes;

namespace Polhem.JsonRpc.UnitTests;

/// <summary>
/// The examples of the JSON-RPC 2.0 specification (https://www.jsonrpc.org/specification#examples), with method
/// names in the <c>ProgId.Action</c> form (<c>Spec.Subtract</c>) and results that are response objects
/// (<c>{"difference": 19}</c> where the specification has <c>19</c>). The specification's positional-parameter
/// examples are answered with -32602 here, because an action takes one request object; that case has its own test.
/// </summary>
public class SpecificationExampleTests
{
    private readonly Server.JsonRpcDispatcher _dispatcher = DispatcherFixture.Create();

    [Theory(DisplayName = "Spec examples: each request gets the answer the specification gives")]
    [InlineData(
        """{"jsonrpc": "2.0", "method": "Spec.Subtract", "params": {"subtrahend": 23, "minuend": 42}, "id": 3}""",
        """{"jsonrpc": "2.0", "result": {"difference": 19}, "id": 3}""")]
    [InlineData(
        """{"jsonrpc": "2.0", "method": "Spec.Subtract", "params": {"minuend": 42, "subtrahend": 23}, "id": 4}""",
        """{"jsonrpc": "2.0", "result": {"difference": 19}, "id": 4}""")]
    [InlineData(
        """{"jsonrpc": "2.0", "method": "foobar", "id": "1"}""",
        """{"jsonrpc": "2.0", "error": {"code": -32601, "message": "Method not found"}, "id": "1"}""")]
    [InlineData(
        """{"jsonrpc": "2.0", "method": "foobar, "params": "bar", "baz]""",
        """{"jsonrpc": "2.0", "error": {"code": -32700, "message": "Parse error"}, "id": null}""")]
    [InlineData(
        """{"jsonrpc": "2.0", "method": 1, "params": "bar"}""",
        """{"jsonrpc": "2.0", "error": {"code": -32600, "message": "Invalid Request"}, "id": null}""")]
    [InlineData(
        """[{"jsonrpc": "2.0", "method": "sum", "params": [1,2,4], "id": "1"}, {"jsonrpc": "2.0", "method" ]""",
        """{"jsonrpc": "2.0", "error": {"code": -32700, "message": "Parse error"}, "id": null}""")]
    [InlineData(
        """[]""",
        """{"jsonrpc": "2.0", "error": {"code": -32600, "message": "Invalid Request"}, "id": null}""")]
    [InlineData(
        """[1]""",
        """[{"jsonrpc": "2.0", "error": {"code": -32600, "message": "Invalid Request"}, "id": null}]""")]
    [InlineData(
        """[1,2,3]""",
        """
        [
            {"jsonrpc": "2.0", "error": {"code": -32600, "message": "Invalid Request"}, "id": null},
            {"jsonrpc": "2.0", "error": {"code": -32600, "message": "Invalid Request"}, "id": null},
            {"jsonrpc": "2.0", "error": {"code": -32600, "message": "Invalid Request"}, "id": null}
        ]
        """)]
    [InlineData(
        """
        [
            {"jsonrpc": "2.0", "method": "Spec.Subtract", "params": {"minuend": 42, "subtrahend": 23}, "id": "1"},
            {"jsonrpc": "2.0", "method": "Spec.Update", "params": {"text": "batch"}},
            {"jsonrpc": "2.0", "method": "Spec.Subtract", "params": {"minuend": 42, "subtrahend": 23}, "id": "2"},
            {"foo": "boo"},
            {"jsonrpc": "2.0", "method": "foo.get", "params": {"name": "myself"}, "id": "5"},
            {"jsonrpc": "2.0", "method": "Spec.Subtract", "params": {"minuend": 7, "subtrahend": 0}, "id": "9"}
        ]
        """,
        """
        [
            {"jsonrpc": "2.0", "result": {"difference": 19}, "id": "1"},
            {"jsonrpc": "2.0", "result": {"difference": 19}, "id": "2"},
            {"jsonrpc": "2.0", "error": {"code": -32600, "message": "Invalid Request"}, "id": null},
            {"jsonrpc": "2.0", "error": {"code": -32601, "message": "Method not found"}, "id": "5"},
            {"jsonrpc": "2.0", "result": {"difference": 7}, "id": "9"}
        ]
        """)]
    public async Task Dispatch_SpecificationExample_ReturnsSpecifiedAnswer(string request, string expected)
    {
        using var answer = await DispatcherFixture.RunAsync(_dispatcher, request);

        Assert.NotNull(answer);
        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(answer.RootElement.GetRawText()), JsonNode.Parse(expected)),
            $"Expected {expected} but got {answer.RootElement.GetRawText()}");
    }

    [Theory(DisplayName = "Spec examples: a notification, alone or in a batch of notifications, gets no answer")]
    [InlineData("""{"jsonrpc": "2.0", "method": "Spec.Update", "params": {"text": "single"}}""")]
    [InlineData("""
        [
            {"jsonrpc": "2.0", "method": "Spec.Update", "params": {"text": "first"}},
            {"jsonrpc": "2.0", "method": "Spec.Update", "params": {"text": "second"}}
        ]
        """)]
    public async Task Dispatch_OnlyNotifications_ReturnsNothing(string request)
    {
        using var answer = await DispatcherFixture.RunAsync(_dispatcher, request);

        Assert.Null(answer);
    }

    [Fact(DisplayName = "A notification runs its method even though it is not answered")]
    public async Task Dispatch_Notification_RunsMethod()
    {
        var marker = Guid.NewGuid().ToString();

        await DispatcherFixture.RunAsync(_dispatcher, $$$"""{"jsonrpc": "2.0", "method": "Spec.Update", "params": {"text": "{{{marker}}}"}}""");

        Assert.Contains(marker, SpecTarget.Updates);
    }

    [Fact(DisplayName = "A notification that fails is not answered either")]
    public async Task Dispatch_FailingNotification_ReturnsNothing()
    {
        using var answer = await DispatcherFixture.RunAsync(_dispatcher, """{"jsonrpc": "2.0", "method": "nothing.here"}""");

        Assert.Null(answer);
    }

    [Theory(DisplayName = "An invalid request object is answered with -32600")]
    [InlineData("""{"method": "Spec.Subtract", "id": 1}""")]
    [InlineData("""{"jsonrpc": "1.0", "method": "Spec.Subtract", "id": 1}""")]
    [InlineData("""{"jsonrpc": "2.0", "id": 1}""")]
    [InlineData("""{"jsonrpc": "2.0", "method": "Spec.Subtract", "params": 5, "id": 1}""")]
    [InlineData("""{"jsonrpc": "2.0", "method": "Spec.Subtract", "params": null, "id": 1}""")]
    public async Task Dispatch_InvalidRequestObject_ReturnsInvalidRequestWithItsId(string request)
    {
        using var answer = await DispatcherFixture.RunAsync(_dispatcher, request);

        Assert.Equal(JsonRpcErrorCodes.InvalidRequest, answer!.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(1, answer.RootElement.GetProperty("id").GetInt32());
    }

    [Theory(DisplayName = "An id that is not a string, an integer or null makes the request invalid, answered with a null id")]
    [InlineData("""{"jsonrpc": "2.0", "method": "Spec.Subtract", "id": 1.5}""")]
    [InlineData("""{"jsonrpc": "2.0", "method": "Spec.Subtract", "id": {"a": 1}}""")]
    [InlineData("""{"jsonrpc": "2.0", "method": "Spec.Subtract", "id": true}""")]
    public async Task Dispatch_InvalidId_ReturnsInvalidRequestWithNullId(string request)
    {
        using var answer = await DispatcherFixture.RunAsync(_dispatcher, request);

        Assert.Equal(JsonRpcErrorCodes.InvalidRequest, answer!.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(JsonValueKind.Null, answer.RootElement.GetProperty("id").ValueKind);
    }

    [Fact(DisplayName = "Positional parameters are answered with -32602, because a method takes one parameter object")]
    public async Task Dispatch_PositionalParams_ReturnsInvalidParams()
    {
        using var answer = await DispatcherFixture.RunAsync(_dispatcher, """{"jsonrpc": "2.0", "method": "Spec.Subtract", "params": [42, 23], "id": 1}""");

        Assert.Equal(JsonRpcErrorCodes.InvalidParams, answer!.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact(DisplayName = "A request with a null id is answered, with a null id")]
    public async Task Dispatch_NullId_IsAnswered()
    {
        using var answer = await DispatcherFixture.RunAsync(_dispatcher, """{"jsonrpc": "2.0", "method": "Spec.Subtract", "params": {"minuend": 2, "subtrahend": 1}, "id": null}""");

        Assert.Equal(1, answer!.RootElement.GetProperty("result").GetProperty("difference").GetInt32());
        Assert.Equal(JsonValueKind.Null, answer.RootElement.GetProperty("id").ValueKind);
    }
}
