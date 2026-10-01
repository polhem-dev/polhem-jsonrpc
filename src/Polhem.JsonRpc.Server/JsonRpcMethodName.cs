namespace Polhem.JsonRpc.Server;

/// <summary>
/// Splits a method name of the form <c>ProgId.Action</c>.
/// </summary>
/// <remarks>
/// A ProgId holds letters, digits, underscores and hyphens; an action holds letters, digits and underscores; each part
/// is at most 64 characters. Anything else is not a method name, which is answered as an unknown method.
/// </remarks>
internal static class JsonRpcMethodName
{
    private const int MaxPartLength = 64;

    public static bool TryParse(string method, out string progId, out string action)
    {
        progId = string.Empty;
        action = string.Empty;
        var dot = method.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0) { return false; }

        var first = method[..dot];
        var second = method[(dot + 1)..];
        if (!IsValidPart(first, allowHyphen: true) || !IsValidPart(second, allowHyphen: false)) { return false; }

        progId = first;
        action = second;
        return true;
    }

    private static bool IsValidPart(string part, bool allowHyphen)
    {
        if (part.Length == 0 || part.Length > MaxPartLength) { return false; }
        foreach (var c in part)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c == '_' || (allowHyphen && c == '-'))) { return false; }
        }
        return true;
    }
}
