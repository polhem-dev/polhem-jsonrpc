using Polhem.JsonRpc;
using Polhem.JsonRpc.Server;

namespace QuickStart.Server;

/// <summary>
/// The methods behind <c>math.add</c>, <c>math.divide</c> and <c>math.log</c>.
/// </summary>
public sealed class Calculator(ILogger<Calculator> logger)
{
    [JsonRpcMethod]
    public int Add(AddArgs args) => args.A + args.B;

    [JsonRpcMethod]
    public double Divide(DivideArgs args) => args.Divisor == 0
        ? throw new JsonRpcErrorException(-32001, "Division by zero")
        : args.Dividend / args.Divisor;

    [JsonRpcMethod]
    public void Log(LogArgs args) => logger.LogInformation("Client says: {Message}", args.Message);
}
