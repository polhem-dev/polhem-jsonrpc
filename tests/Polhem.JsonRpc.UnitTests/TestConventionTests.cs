using System.Reflection;

namespace Polhem.JsonRpc.UnitTests;

/// <summary>
/// Holds every test in this assembly to the conventions in CONTRIBUTING.md, so that a test report always reads as
/// sentences and a failing test's method name still says what broke.
/// </summary>
public class TestConventionTests
{
    // Non-public and static test methods count too: xUnit runs a [Fact] whatever its accessibility.
    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static TheoryData<string> TestMethods =>
    [
        .. typeof(TestConventionTests).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(Declared))
            .Where(method => method.GetCustomAttribute<FactAttribute>() is not null)
            .Select(method => $"{method.DeclaringType!.Name}.{method.Name}"),
    ];

    [Theory(DisplayName = "Every test has a display name on its [Fact] or [Theory], which is what xUnit reports")]
    [MemberData(nameof(TestMethods))]
    public void FactAttribute_EachTest_HasDisplayName(string test)
    {
        Assert.False(string.IsNullOrWhiteSpace(Find(test).GetCustomAttribute<FactAttribute>()!.DisplayName));
    }

    [Theory(DisplayName = "Every test method is named <Method>_<Scenario>_<Expected>")]
    [MemberData(nameof(TestMethods))]
    public void MethodName_EachTest_HasThreeParts(string test)
    {
        Assert.Equal(3, Find(test).Name.Split('_').Length);
    }

    private static MethodInfo Find(string test)
    {
        var dot = test.IndexOf('.', StringComparison.Ordinal);
        return typeof(TestConventionTests).Assembly.GetTypes()
            .Where(type => type.Name == test[..dot])
            .SelectMany(type => type.GetMethods(Declared))
            .Single(method => method.Name == test[(dot + 1)..] && method.GetCustomAttribute<FactAttribute>() is not null);
    }
}
