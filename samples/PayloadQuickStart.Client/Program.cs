using System.Security.Cryptography;
using System.Text;
using Polhem.JsonRpc;
using Polhem.JsonRpc.Client;
using Polhem.JsonRpc.Payload;
using Polhem.JsonRpc.Payload.Client;
using QuickStart.Contracts;

var base64Key = Environment.GetEnvironmentVariable("PayloadDemoKey");
if (string.IsNullOrEmpty(base64Key))
{
    Console.Error.WriteLine("Set PayloadDemoKey to the same Base64 key the server uses.");
    return 1;
}

// The server counts sequence numbers per client, and derives each client's key from the demo key and its id the same
// way, so a call replayed under another client id does not decrypt.
var clientId = Guid.NewGuid().ToString("N");
var key = HMACSHA512.HashData(Convert.FromBase64String(base64Key), Encoding.UTF8.GetBytes(clientId));

using var http = new HttpClient { BaseAddress = new Uri(args.FirstOrDefault() ?? "http://localhost:5081/api") };
http.DefaultRequestHeaders.Add("X-Client-Id", clientId);
var connector = new JsonRpcConnector(new HttpTransport(http));

// The same options as the server: frames on.
var payload = new PayloadProcessor(new PayloadOptions { RequireFrame = true });
var rpc = new PayloadConnector(connector, payload, new PayloadConnectorOptions { KeyProvider = () => key });

// Each call is serialized, compressed, framed with the next sequence number and encrypted, and its result opened.
var added = await rpc.InvokeAsync<AddResponse>("Calculator.Add", new AddRequest { A = 1, B = 2 });
Console.WriteLine($"1 + 2 = {added!.Sum}");
var sum = await rpc.InvokeAsync<AddResponse>("Calculator.Add", new AddRequest { A = 2, B = 3 });
Console.WriteLine($"2 + 3 = {sum!.Sum}");

// A connector that numbers its calls from 1 again, under the same client id, sends a number the server has seen.
var restarted = new PayloadConnector(connector, payload, new PayloadConnectorOptions { KeyProvider = () => key });
try
{
    await restarted.InvokeAsync<AddResponse>("Calculator.Add", new AddRequest { A = 1, B = 2 });
}
catch (JsonRpcErrorException ex)
{
    Console.WriteLine($"Repeated sequence number refused: {ex.Code} {ex.Message}");
}
return 0;
