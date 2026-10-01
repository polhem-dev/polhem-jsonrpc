namespace QuickStart.Client;

public sealed record AddRequest(int A, int B);

public sealed record AddResponse(int Sum);

public sealed record DivideRequest(double Dividend, double Divisor);

public sealed record DivideResponse(double Quotient);

public sealed record LogRequest(string Message);
