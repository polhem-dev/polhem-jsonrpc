namespace QuickStart.Contracts;

/// <summary>
/// Request of <c>Calculator.Add</c>.
/// </summary>
public sealed class AddRequest
{
    /// <summary>
    /// Gets or sets the first addend.
    /// </summary>
    public int A { get; set; }

    /// <summary>
    /// Gets or sets the second addend.
    /// </summary>
    public int B { get; set; }
}
