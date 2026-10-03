using System.ComponentModel;
using System.Reflection;

namespace Polhem.JsonRpc.UnitTests;

/// <summary>
/// The packages reference nothing but .NET, and ASP.NET Core for Polhem.JsonRpc.AspNetCore. The build-time gate in
/// src/Directory.Build.targets checks the package references; this test checks the assemblies actually referenced.
/// </summary>
public class DependencyGateTests
{
    private static readonly string[] s_dotNet = ["System", "netstandard", "Polhem.JsonRpc"];
    private static readonly string[] s_aspNetCore = [.. s_dotNet, "Microsoft.AspNetCore", "Microsoft.Extensions", "Microsoft.Net.Http.Headers"];

    public static TheoryData<string, string[]> Packages => new()
    {
        { typeof(JsonRpcRequest).Assembly.GetName().Name!, s_dotNet },
        { typeof(Server.JsonRpcDispatcher).Assembly.GetName().Name!, s_dotNet },
        { typeof(Client.JsonRpcConnector).Assembly.GetName().Name!, s_dotNet },
        { typeof(Polhem.JsonRpc.Payload.PayloadProcessor).Assembly.GetName().Name!, s_dotNet },
        { typeof(Polhem.JsonRpc.Payload.Server.PayloadFilter).Assembly.GetName().Name!, s_dotNet },
        { typeof(AspNetCore.JsonRpcHttpHandler).Assembly.GetName().Name!, s_aspNetCore },
    };

    [Theory]
    [MemberData(nameof(Packages))]
    [DisplayName("Each package references only .NET (and ASP.NET Core for the ASP.NET Core package)")]
    public void ReferencedAssemblies_AreAllowed(string assemblyName, string[] allowedPrefixes)
    {
        var assembly = Assembly.Load(assemblyName);

        var unexpected = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => !allowedPrefixes.Any(prefix => name == prefix || name.StartsWith(prefix + ".", StringComparison.Ordinal)))
            .ToList();

        Assert.Empty(unexpected);
    }
}
