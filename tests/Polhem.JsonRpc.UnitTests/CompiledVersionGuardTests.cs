using System.Reflection;
using Polhem.JsonRpc.Server;

namespace Polhem.JsonRpc.UnitTests;

public class CompiledVersionGuardTests
{
    private static readonly Version s_running = new(1, 1, 0, 0);

    [Theory(DisplayName = "Version guard: any assembly compiled against Polhem.JsonRpc.Server before 1.1 is named while 1.1 runs, whatever its name")]
    [InlineData("Polhem.JsonRpc.AspNetCore", "1.0.0.0", "1.0.0.0")]
    [InlineData("Polhem.Api.Core", "1.2.0.0", "1.0.0.0")]
    [InlineData("MyApp.Api", "3.0.0.0", "1.0.0.0")]
    [InlineData("Polhem.JsonRpc.AspNetCore", "0.1.0.0", "0.1.0.0")]
    public void FindStale_ReferenceBefore11_IsNamed(string assembly, string version, string referenceVersion)
    {
        var stale = CompiledVersionGuard.FindStale(
            [Entry(assembly, version, ("Polhem.JsonRpc.Server", referenceVersion))], s_running);

        Assert.Equal([$"{assembly} {version}"], stale);
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
    public void FindStaleInProcess_TestProcess_IsEmpty()
    {
        Assert.Empty(CompiledVersionGuard.FindStaleInProcess());
    }

    [Fact(DisplayName = "Version guard: the dispatcher refuses to start while a stale assembly is loaded, naming it")]
    public void Dispatcher_StaleAssembly_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new JsonRpcDispatcher(new JsonRpcServerOptions(), new TestObjectFactory(), () => ["Polhem.JsonRpc.AspNetCore 1.0.0.0"]));

        Assert.Contains("Polhem.JsonRpc.AspNetCore 1.0.0.0", ex.Message, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "Version guard: AllowCodeCompiledAgainst10 no longer lets the dispatcher start beside stale code")]
    [InlineData("Polhem.JsonRpc.AspNetCore 1.0.0.0")]
    [InlineData("Polhem.Api.Core 1.2.0.0")]
    [InlineData("MyApp.Api 1.0.0.0")]
    public void Dispatcher_StaleAssemblyEvenIfAllowed_Throws(string assembly)
    {
        // The obsolete option is set on purpose: the test pins that it no longer has an effect.
#pragma warning disable CS0618
        var options = new JsonRpcServerOptions { AllowCodeCompiledAgainst10 = true };
#pragma warning restore CS0618

        var ex = Assert.Throws<InvalidOperationException>(
            () => new JsonRpcDispatcher(options, new TestObjectFactory(), () => [assembly]));

        Assert.Contains(assembly, ex.Message, StringComparison.Ordinal);
    }

    private static (AssemblyName, AssemblyName[]) Entry(string name, string version, params (string Name, string Version)[] references) =>
        (new AssemblyName(name) { Version = new Version(version) },
         [.. references.Select(reference => new AssemblyName(reference.Name) { Version = new Version(reference.Version) })]);
}
