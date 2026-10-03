using System.Text.RegularExpressions;

namespace Polhem.JsonRpc.UnitTests;

/// <summary>
/// Holds the C# snippets of the READMEs to the code in tests/Polhem.JsonRpc.ReadmeSnippets, which the build compiles
/// against the packages. Each snippet has a region there named <c>readme: &lt;path&gt;#&lt;n&gt;</c>; a snippet may be
/// split over several regions of that name, because statements and type declarations compile in different places.
/// </summary>
public partial class ReadmeSnippetTests
{
    private static readonly string s_root = RepositoryRoot();

    public static TheoryData<string> Blocks => [.. ReadmeBlocks().Keys];

    public static TheoryData<string> TranslatedReadmes =>
    [
        .. ReadmeBlocks().Keys.Select(key => key[..key.LastIndexOf('#')]).Distinct()
            .Where(path => File.Exists(Path.Combine(s_root, Translation(path)))),
    ];

    [Theory(DisplayName = "Every C# snippet of a README is the code of its region in the compiled snippet project")]
    [MemberData(nameof(Blocks))]
    public void ReadmeBlock_EachBlock_MatchesItsRegion(string key)
    {
        var parts = Regions().GetValueOrDefault(key);
        Assert.True(parts is not null, $"No region 'readme: {key}' in tests/Polhem.JsonRpc.ReadmeSnippets.");

        var expected = Lines(ReadmeBlocks()[key]);
        // A README may show the statements before or after the types, so the parts are tried in every order.
        Assert.Contains(Permutations(parts), order => Matches(expected, [.. order.SelectMany(part => part)]));
    }

    [Fact(DisplayName = "Every region of the snippet project belongs to a C# snippet of a README")]
    public void SnippetRegion_EachRegion_HasReadmeBlock()
    {
        Assert.Empty(Regions().Keys.Except(ReadmeBlocks().Keys));
    }

    [Theory(DisplayName = "A translated README shows the same C# code as the English one, apart from comments")]
    [MemberData(nameof(TranslatedReadmes))]
    public void TranslatedReadme_EachBlock_HasSameCode(string path)
    {
        var english = CodeBlocks(File.ReadAllText(Path.Combine(s_root, path)));
        var translated = CodeBlocks(File.ReadAllText(Path.Combine(s_root, Translation(path))));

        Assert.Equal(english.Select(WithoutComments), translated.Select(WithoutComments));
    }

    private static Dictionary<string, string> ReadmeBlocks()
    {
        var excluded = new[] { "local", "maintainers", ".claude", "bin", "obj", "node_modules", ".git" };
        return Directory.EnumerateFiles(s_root, "*.md", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(s_root, file).Replace('\\', '/'))
            .Where(path => !path.EndsWith(".zh-TW.md", StringComparison.Ordinal)
                && !path.StartsWith("CHANGELOG", StringComparison.Ordinal)
                && !path.Split('/').Any(segment => excluded.Contains(segment)))
            .Order(StringComparer.Ordinal)
            .SelectMany(path => CodeBlocks(File.ReadAllText(Path.Combine(s_root, path)))
                .Select((block, index) => (Key: $"{path}#{index + 1}", Block: block)))
            .ToDictionary(entry => entry.Key, entry => entry.Block, StringComparer.Ordinal);
    }

    private static Dictionary<string, List<string[]>> Regions()
    {
        var regions = new Dictionary<string, List<string[]>>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(s_root, "tests", "Polhem.JsonRpc.ReadmeSnippets"), "*.cs").Order(StringComparer.Ordinal))
        {
            string? key = null;
            var lines = new List<string>();
            foreach (var line in File.ReadAllLines(file).Select(line => line.Trim()))
            {
                if (line.StartsWith("#region readme: ", StringComparison.Ordinal))
                {
                    key = line["#region readme: ".Length..];
                    lines = [];
                }
                else if (key is not null && line == "#endregion")
                {
                    if (!regions.TryGetValue(key, out var parts)) { regions[key] = parts = []; }
                    parts.Add(Lines(string.Join('\n', lines)));
                    key = null;
                }
                else if (key is not null)
                {
                    lines.Add(line);
                }
            }
        }
        return regions;
    }

    // A README line may hold a `/* ... */` placeholder where the region has code; the placeholder matches any text.
    private static bool Matches(string[] readme, string[] region) =>
        readme.Length == region.Length
        && readme.Zip(region).All(pair => Regex.IsMatch(pair.Second,
            "^" + PlaceholderPattern().Replace(Regex.Escape(pair.First), ".*") + "$", RegexOptions.None, TimeSpan.FromSeconds(1)));

    private static IEnumerable<List<string[]>> Permutations(List<string[]> parts) =>
        parts.Count <= 1
            ? [parts]
            : parts.SelectMany((part, index) =>
                Permutations([.. parts.Where((_, other) => other != index)]).Select(rest => (List<string[]>)[part, .. rest]));

    private static string[] Lines(string code) =>
        [.. code.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0)];

    private static string WithoutComments(string code) =>
        string.Join('\n', Lines(BlockComment().Replace(LineComment().Replace(code, ""), "")));

    private static string[] CodeBlocks(string markdown) =>
        [.. CodeBlock().Matches(markdown.Replace("\r\n", "\n")).Select(match => match.Groups[1].Value)];

    private static string Translation(string path) => path[..^".md".Length] + ".zh-TW.md";

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Polhem.JsonRpc.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException("The repository root was not found.");
    }

    [GeneratedRegex(@"```csharp\n(.*?)```", RegexOptions.Singleline)]
    private static partial Regex CodeBlock();

    // An escaped `/* ... */`, as Regex.Escape writes it.
    [GeneratedRegex(@"/\\\*.*?\*/")]
    private static partial Regex PlaceholderPattern();

    // A comment after code or on a line of its own; `//` inside a URL is not preceded by whitespace.
    [GeneratedRegex(@"(^|\s)//.*$", RegexOptions.Multiline)]
    private static partial Regex LineComment();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockComment();
}
