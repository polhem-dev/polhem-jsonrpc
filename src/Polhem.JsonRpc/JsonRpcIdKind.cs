namespace Polhem.JsonRpc;

/// <summary>
/// The kinds of value a JSON-RPC <c>id</c> member can hold.
/// </summary>
public enum JsonRpcIdKind
{
    /// <summary>
    /// The <c>id</c> member is absent. A request without an id is a notification.
    /// </summary>
    None,

    /// <summary>
    /// The <c>id</c> member is present and <c>null</c>.
    /// </summary>
    Null,

    /// <summary>
    /// The <c>id</c> member is a string.
    /// </summary>
    String,

    /// <summary>
    /// The <c>id</c> member is an integer number.
    /// </summary>
    Number,
}
