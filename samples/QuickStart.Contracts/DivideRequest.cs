namespace QuickStart.Contracts;

/// <summary>
/// Request of <c>Calculator.Divide</c>.
/// </summary>
public sealed class DivideRequest
{
    /// <summary>
    /// Gets or sets the number to divide.
    /// </summary>
    public double Dividend { get; set; }

    /// <summary>
    /// Gets or sets the number to divide by.
    /// </summary>
    public double Divisor { get; set; }
}
