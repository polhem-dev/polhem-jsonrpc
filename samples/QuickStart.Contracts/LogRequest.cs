namespace QuickStart.Contracts;

/// <summary>
/// Request of <c>Calculator.Log</c>.
/// </summary>
public sealed class LogRequest
{
    /// <summary>
    /// Gets or sets the message to log.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
