using QuickStart.Contracts;

namespace PayloadQuickStart.Server;

/// <summary>
/// The method behind <c>Calculator.Add</c>. It knows nothing about the envelope: the payload filter opens the request
/// before the call and seals the result after it.
/// </summary>
public sealed class Calculator
{
    public AddResponse Add(AddRequest request) => new() { Sum = request.A + request.B };
}
