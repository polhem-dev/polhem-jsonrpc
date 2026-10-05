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

    [Fact(DisplayName = "Version guard: AllowCodeCompiledAgainst10 lets the dispatcher start beside stale application code")]
    public void Dispatcher_StaleAssemblyAllowed_Starts()
    {
        var options = new JsonRpcServerOptions { AllowCodeCompiledAgainst10 = true };

        Assert.NotNull(new JsonRpcDispatcher(options, new TestObjectFactory(), () => ["MyApp.Api 1.0.0.0"]));
    }

    [Theory(DisplayName = "Version guard: AllowCodeCompiledAgainst10 does not cover a stale Polhem.JsonRpc package")]
    [InlineData("Polhem.JsonRpc.AspNetCore 1.0.0.0")]
    [InlineData("Polhem.JsonRpc.Payload.Server 1.0.0.0")]
    public void Dispatcher_StalePackageAllowed_Throws(string package)
    {
        var options = new JsonRpcServerOptions { AllowCodeCompiledAgainst10 = true };

        var ex = Assert.Throws<InvalidOperationException>(
            () => new JsonRpcDispatcher(options, new TestObjectFactory(), () => ["MyApp.Api 1.0.0.0", package]));

        Assert.Contains(package, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("MyApp.Api", ex.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Version guard: the public constructor starts in a process where no stale assembly is loaded")]
    public void Dispatcher_PublicConstructor_StartsWithoutStaleAssembly()
    {
        // A stale assembly cannot be loaded here without making every other dispatcher of the run refuse to start, so
        // that the public constructors pass the real check is held by reading them, not by this test.
        Assert.NotNull(new JsonRpcDispatcher(new JsonRpcServerOptions { ObjectFactory = new TestObjectFactory() }));
    }

    private static (AssemblyName, AssemblyName[]) Entry(string name, string version, params (string Name, string Version)[] references) =>
        (new AssemblyName(name) { Version = new Version(version) },
         [.. references.Select(reference => new AssemblyName(reference.Name) { Version = new Version(reference.Version) })]);
}
