namespace QuickStart.Contracts;

/// <summary>
/// Response of <c>Calculator.Log</c>.
/// </summary>
public sealed class LogResponse
{
    /// <summary>
    /// Gets or sets when the server logged the message, in UTC.
    /// </summary>
    public DateTime LoggedAt { get; set; }
}
