using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Polhem.JsonRpc.Server;

/// <summary>
/// Finds code compiled against <c>Polhem.JsonRpc.Server</c> 1.0 while 1.1 or later runs. 1.1 renumbered
/// <see cref="JsonRpcTransportKind"/>, and an enum value is compiled into the code that compares it, so such code takes
/// every HTTP call for an in-process one.
/// </summary>
internal static class CompiledVersionGuard
{
    private const string ServerAssembly = "Polhem.JsonRpc.Server";
    private const string PackagePrefix = "Polhem.JsonRpc.";

    // The version that renumbered JsonRpcTransportKind (ADR-001, decision 4, amended for 1.1.0).
    private static readonly Version s_renumbered = new(1, 1);

    /// <summary>Gets the names of the loaded assemblies compiled against a version before the renumbering.</summary>
    /// <returns>The names, empty when there are none or the running version is itself before 1.1.</returns>
    [RequiresUnreferencedCode("Reads the references of the loaded assemblies, which trimming may remove.")]
    public static IReadOnlyList<string> FindStaleInProcess() => FindStale(
        AppDomain.CurrentDomain.GetAssemblies().Where(assembly => !assembly.IsDynamic)
            .Select(assembly => (assembly.GetName(), assembly.GetReferencedAssemblies())),
        typeof(CompiledVersionGuard).Assembly.GetName().Version);

    /// <summary>Throws when any assembly is named that the opt-out does not cover.</summary>
    /// <param name="stale">The names of the assemblies compiled against an older version.</param>
    /// <param name="allowOthers">
    /// Whether <see cref="JsonRpcServerOptions.AllowCodeCompiledAgainst10"/> is set. It covers the application's code but
    /// never the packages' own assemblies: <c>Polhem.JsonRpc.AspNetCore</c> 1.0 sets the kind of every HTTP call to the
    /// value that 1.1 reads as in-process.
    /// </param>
    /// <exception cref="InvalidOperationException">An assembly the opt-out does not cover is named.</exception>
    public static void ThrowIfAny(IReadOnlyList<string> stale, bool allowOthers)
    {
        var refused = allowOthers ? [.. stale.Where(IsPackageAssembly)] : stale;
        if (refused.Count > 0)
        {
            throw new InvalidOperationException(
                $"These assemblies were compiled against {ServerAssembly} 1.0, whose JsonRpcTransportKind numbers differ: " +
                $"{string.Join(", ", refused)}. Upgrade every Polhem.JsonRpc package, and the code built on them, together. " +
                "JsonRpcServerOptions.AllowCodeCompiledAgainst10 covers only application code that neither reads nor sets " +
                "the transport kind; it never covers the Polhem.JsonRpc packages themselves.");
        }
    }

    private static bool IsPackageAssembly(string name) => name.StartsWith(PackagePrefix, StringComparison.Ordinal);

    /// <summary>Gets the names of the assemblies that reference the server assembly at a version before 1.1.</summary>
    /// <param name="assemblies">Each assembly with the assemblies it references.</param>
    /// <param name="running">The version of the server assembly that runs.</param>
    /// <returns>The names, empty when the running version is itself before 1.1.</returns>
    internal static IReadOnlyList<string> FindStale(IEnumerable<(AssemblyName Assembly, AssemblyName[] References)> assemblies, Version? running)
    {
        if (running is null || running < s_renumbered) { return []; }
        return
        [
            .. assemblies
                .Where(entry => entry.References.Any(reference =>
                    string.Equals(reference.Name, ServerAssembly, StringComparison.Ordinal)
                    && reference.Version is { } version && version < s_renumbered))
                .Select(entry => $"{entry.Assembly.Name} {entry.Assembly.Version}")
                .Order(StringComparer.Ordinal),
        ];
    }
}
