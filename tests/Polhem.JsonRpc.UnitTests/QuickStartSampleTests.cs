using System.ComponentModel;
using Microsoft.AspNetCore.Mvc.Testing;
using Polhem.JsonRpc.Client;
using QuickStart.Server;

namespace Polhem.JsonRpc.UnitTests;

/// <summary>
/// Runs the QuickStart server sample and calls it the way the QuickStart client sample does, so that the samples
/// cannot drift away from the API.
/// </summary>
public sealed class QuickStartSampleTests(WebApplicationFactory<Calculator> factory) : IClassFixture<WebApplicationFactory<Calculator>>
{
    private JsonRpcConnector Connect()
    {
        var http = factory.CreateClient();
        return new JsonRpcConnector(new HttpTransport(http, new Uri("/api", UriKind.Relative)));
    }

    [Fact]
    [DisplayName("QuickStart sample: math.add returns the sum")]
    public async Task Add_ReturnsSum()
    {
        Assert.Equal(3, await Connect().InvokeAsync<int>("math.add", new AddArgs(1, 2)));
    }

    [Fact]
    [DisplayName("QuickStart sample: math.divide by zero answers with error -32001")]
    public async Task Divide_ByZero_ReturnsError()
    {
        var ex = await Assert.ThrowsAsync<JsonRpcErrorException>(() => Connect().InvokeAsync<double>("math.divide", new DivideArgs(1, 0)));

        Assert.Equal(-32001, ex.Code);
    }

    [Fact]
    [DisplayName("QuickStart sample: a notification and a batch work end to end")]
    public async Task NotificationAndBatch_Work()
    {
        var rpc = Connect();
        await rpc.NotifyAsync("math.log", new LogArgs("test"));

        var batch = rpc.CreateBatch();
        var first = batch.Add<int>("math.add", new AddArgs(2, 3));
        var second = batch.Add<int>("math.add", new AddArgs(4, 5));
        await batch.SendAsync();

        Assert.Equal(5, await first);
        Assert.Equal(9, await second);
    }
}
