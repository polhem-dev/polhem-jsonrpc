using Polhem.JsonRpc;

namespace QuickStart.Server;

/// <summary>
/// The methods behind <c>Calculator.Add</c>, <c>Calculator.Divide</c> and <c>Calculator.Log</c>. A method is
/// callable because its parameter is named <c>{Action}Request</c> and its result <c>{Action}Response</c>.
/// </summary>
public sealed class Calculator
{
    public AddResponse Add(AddRequest request) => new(request.A + request.B);

    public DivideResponse Divide(DivideRequest request) => request.Divisor == 0
        ? throw new JsonRpcErrorException(-32001, "Division by zero")
        : new DivideResponse(request.Dividend / request.Divisor);

    public LogResponse Log(LogRequest request)
    {
        Console.WriteLine($"Client says: {request.Message}");
        return new LogResponse();
    }
}
