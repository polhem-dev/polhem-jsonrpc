using System.Text;
using System.Text.Json;
using Polhem.JsonRpc.Server;

namespace Polhem.JsonRpc.UnitTests;

internal static class DispatcherFixture
{
    public static JsonRpcServerOptions Options() => new JsonRpcServerOptions()
        .AddTarget<SpecTarget>("spec")
        .AddTarget<DisposableTarget>("disposable");

    public static JsonRpcDispatcher Create(Action<JsonRpcServerOptions>? configure = null)
    {
        var options = Options();
        configure?.Invoke(options);
        return new JsonRpcDispatcher(options);
    }

    public static JsonRpcTransportInfo Http(IReadOnlyDictionary<string, string>? headers = null) =>
        new(JsonRpcTransportKind.Http, headers: headers);

    /// <summary>
    /// Runs a message and returns the answer as a JSON document, or <c>null</c> when nothing is sent back.
    /// </summary>
    public static async Task<JsonDocument?> RunAsync(JsonRpcDispatcher dispatcher, string json, JsonRpcTransportInfo? transport = null)
    {
        var result = await dispatcher.DispatchMessageAsync(Encoding.UTF8.GetBytes(json), transport ?? Http());
        var bytes = result.Serialize();
        return bytes is null ? null : JsonDocument.Parse(bytes);
    }

    public static JsonElement Element(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
