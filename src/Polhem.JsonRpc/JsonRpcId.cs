using System.Globalization;

namespace Polhem.JsonRpc;

/// <summary>
/// The <c>id</c> of a JSON-RPC request or response: absent, <c>null</c>, a string or an integer number.
/// </summary>
/// <remarks>
/// The default value is <see cref="None"/>, the id of a notification. The specification discourages fractional
/// numbers as ids; they are rejected as an invalid request.
/// </remarks>
public readonly struct JsonRpcId : IEquatable<JsonRpcId>
{
    private readonly string? _string;
    private readonly long _number;

    private JsonRpcId(JsonRpcIdKind kind, string? value, long number)
    {
        Kind = kind;
        _string = value;
        _number = number;
    }

    /// <summary>
    /// Gets the absent id, which marks a notification.
    /// </summary>
    public static JsonRpcId None => default;

    /// <summary>
    /// Gets the <c>null</c> id, used in an error response when the id of the request could not be determined.
    /// </summary>
    public static JsonRpcId Null => new(JsonRpcIdKind.Null, null, 0);

    /// <summary>
    /// Gets the kind of value this id holds.
    /// </summary>
    public JsonRpcIdKind Kind { get; }

    /// <summary>
    /// Gets a value indicating whether the id is absent, which marks a notification.
    /// </summary>
    public bool IsNone => Kind == JsonRpcIdKind.None;

    /// <summary>
    /// Gets the string value. Valid only when <see cref="Kind"/> is <see cref="JsonRpcIdKind.String"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The id is not a string.</exception>
    public string StringValue => Kind == JsonRpcIdKind.String
        ? _string!
        : throw new InvalidOperationException("The id is not a string.");

    /// <summary>
    /// Gets the number value. Valid only when <see cref="Kind"/> is <see cref="JsonRpcIdKind.Number"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The id is not a number.</exception>
    public long NumberValue => Kind == JsonRpcIdKind.Number
        ? _number
        : throw new InvalidOperationException("The id is not a number.");

    /// <summary>
    /// Creates a string id.
    /// </summary>
    /// <param name="value">The id value.</param>
    /// <returns>The id.</returns>
    public static JsonRpcId FromString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new JsonRpcId(JsonRpcIdKind.String, value, 0);
    }

    /// <summary>
    /// Creates a number id.
    /// </summary>
    /// <param name="value">The id value.</param>
    /// <returns>The id.</returns>
    public static JsonRpcId FromNumber(long value) => new(JsonRpcIdKind.Number, null, value);

    /// <summary>
    /// Converts a string to a string id.
    /// </summary>
    /// <param name="value">The id value.</param>
    public static implicit operator JsonRpcId(string value) => FromString(value);

    /// <summary>
    /// Converts a number to a number id.
    /// </summary>
    /// <param name="value">The id value.</param>
    public static implicit operator JsonRpcId(long value) => FromNumber(value);

    /// <summary>
    /// Determines whether two ids are equal.
    /// </summary>
    public static bool operator ==(JsonRpcId left, JsonRpcId right) => left.Equals(right);

    /// <summary>
    /// Determines whether two ids differ.
    /// </summary>
    public static bool operator !=(JsonRpcId left, JsonRpcId right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(JsonRpcId other) => Kind == other.Kind
        && _number == other._number
        && string.Equals(_string, other._string, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is JsonRpcId other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Kind, _number, _string is null ? 0 : StringComparer.Ordinal.GetHashCode(_string));

    /// <inheritdoc/>
    public override string ToString() => Kind switch
    {
        JsonRpcIdKind.String => _string!,
        JsonRpcIdKind.Number => _number.ToString(CultureInfo.InvariantCulture),
        JsonRpcIdKind.Null => "null",
        _ => string.Empty,
    };
}
