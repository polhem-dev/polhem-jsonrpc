using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Polhem.JsonRpc;
using Polhem.JsonRpc.Client;
using Polhem.JsonRpc.Payload;
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
var rpc = new JsonRpcConnector(new HttpTransport(http));

// The same options as the server: frames on.
var payload = new PayloadProcessor(new PayloadOptions { RequireFrame = true });

// An encrypted call: the parameters are serialized, compressed, framed with sequence number 1 and encrypted.
var parameters = payload.WrapRequest("Calculator.Add", new AddRequest { A = 1, B = 2 }, PayloadFormat.Encrypted, key: key, sequence: 1);
var result = await rpc.InvokeAsync<JsonElement>("Calculator.Add", parameters);
var added = payload.UnwrapResult<AddResponse>("Calculator.Add", result, key)!;
Console.WriteLine($"1 + 2 = {added.Sum} (sent as {PayloadEnvelope.ReadFormat(parameters)})");

// Sending the same bytes again is a replay: the server has already seen sequence number 1 from this client.
try
{
    await rpc.InvokeAsync<JsonElement>("Calculator.Add", parameters);
}
catch (JsonRpcErrorException ex)
{
    Console.WriteLine($"Replayed call refused: {ex.Code} {ex.Message}");
}

// The next call takes the next number.
var next = payload.WrapRequest("Calculator.Add", new AddRequest { A = 2, B = 3 }, PayloadFormat.Encrypted, key: key, sequence: 2);
var sum = payload.UnwrapResult<AddResponse>("Calculator.Add", await rpc.InvokeAsync<JsonElement>("Calculator.Add", next), key)!;
Console.WriteLine($"2 + 3 = {sum.Sum}");
return 0;
