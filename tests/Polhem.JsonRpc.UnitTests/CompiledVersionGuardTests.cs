using System.Reflection;
using Polhem.JsonRpc.Server;

namespace Polhem.JsonRpc.UnitTests;

public class CompiledVersionGuardTests
{
    private static readonly Version s_running = new(1, 1, 0, 0);

    [Fact(DisplayName = "Version guard: an assembly compiled against Polhem.JsonRpc.Server 1.0 is named while 1.1 runs")]
    public void FindStale_ReferenceTo10_IsNamed()
    {
        var stale = CompiledVersionGuard.FindStale(
            [Entry("Polhem.JsonRpc.AspNetCore", "1.0.0.0", ("Polhem.JsonRpc.Server", "1.0.0.0"))], s_running);

        Assert.Equal(["Polhem.JsonRpc.AspNetCore 1.0.0.0"], stale);
    }

    [Theory(DisplayName = "Version guard: references to 1.1 or later, to other assemblies, or from a running 1.0 are not named")]
    [InlineData("Polhem.JsonRpc.Server", "1.1.0.0", "1.1.0.0")]
    [InlineData("Polhem.JsonRpc.Server", "2.0.0.0", "2.0.0.0")]
    [InlineData("Polhem.JsonRpc", "1.0.0.0", "1.1.0.0")]
    [InlineData("polhem.jsonrpc.server", "1.0.0.0", "1.1.0.0")]
    [InlineData("Polhem.JsonRpc.Server", "1.0.0.0", "1.0.0.0")]
    public void FindStale_NotStale_IsEmpty(string reference, string referenceVersion, string running)
    {
        var stale = CompiledVersionGuard.FindStale([Entry("App", "1.0.0.0", (reference, referenceVersion))], new Version(running));

        Assert.Empty(stale);
    }

    [Fact(DisplayName = "Version guard: none of the assemblies loaded in the tests is compiled against an older server")]
    public void ThrowIfStale_TestProcess_DoesNotThrow()
    {
        CompiledVersionGuard.ThrowIfStale();
    }

    private static (AssemblyName, AssemblyName[]) Entry(string name, string version, params (string Name, string Version)[] references) =>
        (new AssemblyName(name) { Version = new Version(version) },
         [.. references.Select(reference => new AssemblyName(reference.Name) { Version = new Version(reference.Version) })]);
}
