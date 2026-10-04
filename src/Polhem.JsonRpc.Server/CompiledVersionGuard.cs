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

    /// <summary>Throws when a loaded assembly was compiled against a version before the renumbering.</summary>
    /// <exception cref="InvalidOperationException">An assembly compiled against an older version is loaded.</exception>
    [RequiresUnreferencedCode("Reads the references of the loaded assemblies, which trimming may remove.")]
    public static void ThrowIfStale()
    {
        var stale = FindStale(
            AppDomain.CurrentDomain.GetAssemblies().Where(assembly => !assembly.IsDynamic)
                .Select(assembly => (assembly.GetName(), assembly.GetReferencedAssemblies())),
            typeof(CompiledVersionGuard).Assembly.GetName().Version);
        if (stale.Count > 0)
        {
            throw new InvalidOperationException(
                $"These assemblies were compiled against {ServerAssembly} 1.0, whose JsonRpcTransportKind numbers differ: " +
                $"{string.Join(", ", stale)}. Upgrade every Polhem.JsonRpc package, and the code built on them, together; " +
                "set JsonRpcServerOptions.AllowCodeCompiledAgainst10 only for code that never reads the transport kind.");
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
