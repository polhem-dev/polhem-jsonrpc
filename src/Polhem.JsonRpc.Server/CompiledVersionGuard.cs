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

    // The version that renumbered JsonRpcTransportKind (ADR-001, decision 4, amended for 1.1.0).
    private static readonly Version s_renumbered = new(1, 1);

    /// <summary>Gets the names of the loaded assemblies compiled against a version before the renumbering.</summary>
    /// <returns>The names, empty when there are none or the running version is itself before 1.1.</returns>
    [RequiresUnreferencedCode("Reads the references of the loaded assemblies, which trimming may remove.")]
    public static IReadOnlyList<string> FindStaleInProcess() => FindStale(
        AppDomain.CurrentDomain.GetAssemblies().Where(assembly => !assembly.IsDynamic)
            .Select(assembly => (assembly.GetName(), assembly.GetReferencedAssemblies())),
        typeof(CompiledVersionGuard).Assembly.GetName().Version);

    /// <summary>Throws when any assembly is named.</summary>
    /// <param name="stale">The names of the assemblies compiled against an older version.</param>
    /// <exception cref="InvalidOperationException">An assembly is named.</exception>
    public static void ThrowIfAny(IReadOnlyList<string> stale)
    {
        if (stale.Count > 0)
        {
            throw new InvalidOperationException(
                $"These assemblies were compiled against {ServerAssembly} 1.0, whose JsonRpcTransportKind numbers differ: " +
                $"{string.Join(", ", stale)}. Upgrade every Polhem.JsonRpc package, and recompile the code built on them, " +
                "together.");
        }
    }

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
