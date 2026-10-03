namespace Polhem.JsonRpc.UnitTests.Payload;

/// <summary>
/// A body type shaped like the Polhem framework's <c>PingRequest</c>, registered under that type's wire name so that the
/// envelopes captured from Polhem open into it.
/// </summary>
public sealed class VectorPing
{
    public const string PolhemTypeName = "Polhem.Api.Core.Messages.System.PingRequest, Polhem.Api.Core";

    public string? ClientName { get; set; }

    public string? TraceId { get; set; }
}
