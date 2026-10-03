using System.ComponentModel;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;

namespace Polhem.JsonRpc.UnitTests;

/// <summary>
/// The packages reference nothing but .NET, ASP.NET Core for Polhem.JsonRpc.AspNetCore, and each other only along the
/// edges ADR-001 decision 1 and ADR-002 decision 1 allow. The build-time gate in src/Directory.Build.targets checks the
/// package and framework references; these tests check the project references and the assemblies actually referenced,
/// and that every package under src is listed here.
/// </summary>
public class DependencyGateTests
{
    private static readonly string[] s_dotNet = ["System", "netstandard"];
    private static readonly string[] s_aspNetCore = [.. s_dotNet, "Microsoft.AspNetCore", "Microsoft.Extensions", "Microsoft.Net.Http.Headers"];

    // The other packages each package may reference. The client never references the server, and the core packages
    // never reference the payload packages.
    private static readonly Dictionary<string, (string[] Packages, string[] Platform)> s_allowed = new(StringComparer.Ordinal)
    {
        ["Polhem.JsonRpc"] = ([], s_dotNet),
        ["Polhem.JsonRpc.Server"] = (["Polhem.JsonRpc"], s_dotNet),
        ["Polhem.JsonRpc.AspNetCore"] = (["Polhem.JsonRpc", "Polhem.JsonRpc.Server"], s_aspNetCore),
        ["Polhem.JsonRpc.Client"] = (["Polhem.JsonRpc"], s_dotNet),
        ["Polhem.JsonRpc.Payload"] = (["Polhem.JsonRpc"], s_dotNet),
        ["Polhem.JsonRpc.Payload.Server"] = (["Polhem.JsonRpc", "Polhem.JsonRpc.Payload", "Polhem.JsonRpc.Server"], s_dotNet),
    };

    public static TheoryData<string> Packages => [.. s_allowed.Keys];

    [Fact]
    [DisplayName("Every package under src is listed in the dependency gate, so a new package cannot go unchecked")]
    public void Packages_ListEveryProjectUnderSrc()
    {
        var projects = Directory.GetFiles(SourceDirectory(), "*.csproj", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension)
            .Order(StringComparer.Ordinal);

        Assert.Equal(s_allowed.Keys.Order(StringComparer.Ordinal), projects);
    }

    [Theory]
    [MemberData(nameof(Packages))]
    [DisplayName("Each package's project references follow the allowed edges between the packages")]
    public void ProjectReferences_AreAllowed(string package)
    {
        var project = Path.Combine(SourceDirectory(), package, package + ".csproj");
        using var reader = XmlReader.Create(project, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });

        var unexpected = XDocument.Load(reader).Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value.Replace('\\', '/')))
            .Where(name => !s_allowed[package].Packages.Contains(name, StringComparer.Ordinal))
            .ToList();

        Assert.Empty(unexpected);
    }

    [Theory]
    [MemberData(nameof(Packages))]
    [DisplayName("Each package's assembly references only .NET, ASP.NET Core where allowed, and the allowed packages")]
    public void ReferencedAssemblies_AreAllowed(string package)
    {
        var (packages, platform) = s_allowed[package];

        var unexpected = Assembly.Load(package).GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => !packages.Contains(name, StringComparer.Ordinal)
                && !platform.Any(prefix => name == prefix || name.StartsWith(prefix + ".", StringComparison.Ordinal)))
            .ToList();

        Assert.Empty(unexpected);
    }

    private static string SourceDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Polhem.JsonRpc.slnx")))
        {
            directory = directory.Parent;
        }
        return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("The repository root was not found."), "src");
    }
}
