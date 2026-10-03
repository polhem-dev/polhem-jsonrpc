using Polhem.JsonRpc.Server;

namespace Polhem.JsonRpc.UnitTests;

public partial class DispatcherTests
{
    [Fact(DisplayName = "Filters run in order around the call")]
    public async Task DispatchAsync_Filters_RunInOrder()
    {
        var log = new List<string>();
        var dispatcher = DispatcherFixture.Create(o =>
        {
            o.Filters.Add(new RecordingFilter("a", log));
            o.Filters.Add(new RecordingFilter("b", log));
        });

        await CallAsync(dispatcher, "Spec.Subtract", """{"minuend": 1, "subtrahend": 1}""");

        Assert.Equal(["a:before", "b:before", "b:after", "a:after"], log);
    }

    [Fact(DisplayName = "Filters see the resolved object and method, and do not run for a method that is not found")]
    public async Task DispatchAsync_Filters_RunAfterResolution()
    {
        var seen = new List<string>();
        var dispatcher = DispatcherFixture.Create(o => o.Filters.Add(new MethodRecordingFilter(seen)));

        await CallAsync(dispatcher, "Spec.Subtract", """{"minuend": 1, "subtrahend": 1}""");
        await CallAsync(dispatcher, "Spec.Missing", """{"minuend": 1, "subtrahend": 1}""");

        Assert.Equal(["Spec.Subtract:SpecTarget"], seen);
    }

    [Fact(DisplayName = "A filter that throws JsonRpcErrorException rejects the call before the method runs")]
    public async Task DispatchAsync_FilterThrows_RejectsCall()
    {
        var marker = Guid.NewGuid().ToString();
        var dispatcher = DispatcherFixture.Create(o => o.Filters.Add(new RejectingFilter()));

        var response = await CallAsync(dispatcher, "Spec.Update", $$"""{"text": "{{marker}}"}""");

        Assert.Equal(-32001, response.Error!.Code);
        Assert.DoesNotContain(marker, SpecTarget.Updates);
    }

    [Fact(DisplayName = "Filters can rewrite raw params and results: a compress-and-encrypt round trip")]
    public async Task DispatchAsync_PayloadFilter_RewritesParamsAndResult()
    {
        var dispatcher = DispatcherFixture.Create(o => o.Filters.Add(new SealedPayloadFilter()));
        var sealedParams = SealedPayload.Seal(DispatcherFixture.Element("""{"minuend": 10, "subtrahend": 4}"""));

        var response = await CallAsync(dispatcher, "Spec.Subtract", sealedParams.GetRawText());

        Assert.Equal(6, SealedPayload.Open(response.Result!.Value).GetProperty("difference").GetInt32());
    }

    [Fact(DisplayName = "A filter can answer from the returned object, so a result the default options cannot write never reaches them")]
    public async Task DispatchAsync_FilterWritesResultFromReturnValue_SkipsDefaultSerialization()
    {
        var dispatcher = DispatcherFixture.Create(o => o.Filters.Add(new DescribeFilter()));

        var response = await CallAsync(dispatcher, "Spec.Describe", """{"text": "x"}""");

        Assert.Equal("String:x", response.Result!.Value.GetString());
    }

    [Fact(DisplayName = "Without such a filter, a result the default options cannot write is an internal error")]
    public async Task DispatchAsync_UnserializableResultWithoutFilter_ReturnsInternalError()
    {
        var response = await CallAsync(DispatcherFixture.Create(), "Spec.Describe", """{"text": "x"}""");

        Assert.Equal(JsonRpcErrorCodes.InternalError, response.Error!.Code);
    }

    [Fact(DisplayName = "The filter sees the transport kind and items the transport set")]
    public async Task DispatchAsync_Filter_SeesTransportInfo()
    {
        var seen = new List<string>();
        var dispatcher = DispatcherFixture.Create(o => o.Filters.Add(new TransportRecordingFilter(seen)));
        var transport = new JsonRpcTransportInfo(JsonRpcTransportKind.InProcess, items: new Dictionary<string, object?> { ["token"] = "t-1" });

        await CallAsync(dispatcher, "Spec.Subtract", """{"minuend": 1, "subtrahend": 1}""", transport);

        Assert.Equal(["InProcess:t-1"], seen);
    }
}
