using System.Reflection;
using Polhem.JsonRpc.Server;

namespace Polhem.JsonRpc.UnitTests;

public partial class DispatcherTests
{
    [Fact(DisplayName = "ProgId.Action creates the object for the ProgId and calls the action on it")]
    public async Task DispatchAsync_ProgIdAction_CallsAction()
    {
        var response = await CallAsync(DispatcherFixture.Create(), "Spec.Subtract", """{"minuend": 5, "subtrahend": 3}""");

        Assert.Equal(2, response.Result!.Value.GetProperty("difference").GetInt32());
    }

    [Theory(DisplayName = "A name the factory or the object does not know is answered with -32601")]
    [InlineData("Unknown.Subtract")]
    [InlineData("Spec.Missing")]
    public async Task DispatchAsync_UnknownProgIdOrAction_ReturnsMethodNotFound(string method)
    {
        var response = await CallAsync(DispatcherFixture.Create(), method, """{"minuend": 1, "subtrahend": 1}""");

        Assert.Equal(JsonRpcErrorCodes.MethodNotFound, response.Error!.Code);
    }

    [Theory(DisplayName = "Names are matched case-sensitively, as in the Polhem framework")]
    [InlineData("Spec.subtract")]
    [InlineData("spec.Subtract")]
    public async Task DispatchAsync_DifferentCase_ReturnsMethodNotFound(string method)
    {
        var response = await CallAsync(DispatcherFixture.Create(), method, """{"minuend": 1, "subtrahend": 1}""");

        Assert.Equal(JsonRpcErrorCodes.MethodNotFound, response.Error!.Code);
    }

    [Theory(DisplayName = "Malformed or too long names, and methods that are static, overloaded, accessors, generic or declared by object, are not resolved")]
    [InlineData("Spec")]
    [InlineData(".Subtract")]
    [InlineData("Spec.")]
    [InlineData("Spec.Sub-tract")]
    [InlineData("Spec.Subtract.Extra")]
    [InlineData("Spec.Static")]
    [InlineData("Spec.Twice")]
    [InlineData("Spec.ToString")]
    [InlineData("Spec.Equals")]
    [InlineData("Spec.set_Label")]
    [InlineData("Spec.Generic")]
    [InlineData("Spec.LongActionNameThatFillsEveryOneOfTheSixtyFourCharactersAllowed_XY")]
    public async Task DispatchAsync_UnresolvableName_ReturnsMethodNotFound(string method)
    {
        var dispatcher = DispatcherFixture.Create(o => o.MethodPolicy = new AllowAllPolicy());

        var response = await CallAsync(dispatcher, method, """{"minuend": 1, "subtrahend": 1}""");

        Assert.Equal(JsonRpcErrorCodes.MethodNotFound, response.Error!.Code);
    }

    [Theory(DisplayName = "A ProgId or action with a character outside letters, digits, underscores and the ProgId's hyphens is answered with -32601 before the object factory is asked")]
    [InlineData("Sp ec.Subtract")]
    [InlineData("Spec/x.Subtract")]
    [InlineData("Spé.Subtract")]
    [InlineData("Spec.Sub tract")]
    [InlineData("Spec.Subträct")]
    [InlineData("Spec.Sub-tract")]
    public async Task DispatchAsync_DisallowedCharacter_FactoryNotAsked(string method)
    {
        var factory = new RecordingFactory();
        var dispatcher = new JsonRpcDispatcher(new JsonRpcServerOptions { MethodPolicy = new AllowAllPolicy() }, factory);

        var response = await CallAsync(dispatcher, method, """{"minuend": 1, "subtrahend": 1}""");

        Assert.Equal(JsonRpcErrorCodes.MethodNotFound, response.Error!.Code);
        Assert.Empty(factory.Asked);
    }

    [Theory(DisplayName = "A record's Equals is not an action, even under a policy that admits every method")]
    [InlineData("Record.Equals")]
    [InlineData("DerivedRecord.Equals")]
    public async Task DispatchAsync_RecordEquals_ReturnsMethodNotFound(string method)
    {
        var dispatcher = DispatcherFixture.Create(o => o.MethodPolicy = new AllowAllPolicy());

        var response = await CallAsync(dispatcher, method, """{"minuend": 1, "subtrahend": 1}""");

        Assert.Equal(JsonRpcErrorCodes.MethodNotFound, response.Error!.Code);
    }

    [Theory(DisplayName = "A public method inherited from a base type is an action, as with Polhem's Type.GetMethod")]
    [InlineData("Record.Subtract")]
    [InlineData("DerivedRecord.Subtract")]
    public async Task DispatchAsync_InheritedMethod_CallsAction(string method)
    {
        var dispatcher = DispatcherFixture.Create(o => o.MethodPolicy = new AllowAllPolicy());

        var response = await CallAsync(dispatcher, method, """{"minuend": 5, "subtrahend": 3}""");

        Assert.Equal(2, response.Result!.Value.GetProperty("difference").GetInt32());
    }

    [Fact(DisplayName = "An action name of exactly 64 characters is resolved")]
    public async Task DispatchAsync_ActionNameAtLengthLimit_CallsAction()
    {
        var dispatcher = DispatcherFixture.Create(o => o.MethodPolicy = new AllowAllPolicy());

        var response = await CallAsync(dispatcher, "Spec.LongActionNameThatFillsEveryOneOfTheSixtyFourCharactersAllowed_X",
            """{"minuend": 5, "subtrahend": 3}""");

        Assert.Equal(2, response.Result!.Value.GetProperty("difference").GetInt32());
    }

    [Theory(DisplayName = "A ProgId of exactly 64 characters is resolved, and one of 65 is not a method name")]
    [InlineData(TestObjectFactory.LongProgId, false)]
    [InlineData(TestObjectFactory.LongProgId + "X", true)]
    public async Task DispatchAsync_ProgIdAtLengthLimit_IsResolvedUpTo64(string progId, bool refused)
    {
        var response = await CallAsync(DispatcherFixture.Create(), progId + ".Subtract", """{"minuend": 5, "subtrahend": 3}""");

        if (refused)
        {
            Assert.Equal(JsonRpcErrorCodes.MethodNotFound, response.Error!.Code);
        }
        else
        {
            Assert.Equal(2, response.Result!.Value.GetProperty("difference").GetInt32());
        }
    }

    [Fact(DisplayName = "Names the caller makes up are looked up but never cached, so they cannot grow the dispatcher's memory")]
    public async Task DispatchAsync_ManyUnknownActions_CacheHoldsOneEntryPerType()
    {
        var dispatcher = DispatcherFixture.Create();

        for (var i = 0; i < 1000; i++)
        {
            await CallAsync(dispatcher, $"Spec.Unknown{i}", "{}");
        }
        await CallAsync(dispatcher, "Spec.Subtract", """{"minuend": 1, "subtrahend": 1}""");

        // White-box on purpose: the size of the cache is the property, and nothing public reveals it.
        var cache = (System.Collections.ICollection)typeof(JsonRpcDispatcher)
            .GetField("_actions", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dispatcher)!;
        Assert.Single(cache.Cast<object>());
    }

    [Fact(DisplayName = "A public method outside the {Action}Request/{Action}Response convention is not callable")]
    public async Task DispatchAsync_UnconventionalMethod_ReturnsMethodNotFound()
    {
        var response = await CallAsync(DispatcherFixture.Create(), "Spec.Echo", """{"text": "x"}""");

        Assert.Equal(JsonRpcErrorCodes.MethodNotFound, response.Error!.Code);
    }

    [Fact(DisplayName = "A replaced method policy decides which methods are callable")]
    public async Task DispatchAsync_CustomPolicy_AdmitsUnconventionalMethod()
    {
        var dispatcher = DispatcherFixture.Create(o => o.MethodPolicy = new AllowAllPolicy());

        var response = await CallAsync(dispatcher, "Spec.Echo", """{"text": "x"}""");

        Assert.Equal("x", response.Result!.Value.GetString());
    }

    [Theory(DisplayName = "Naming convention: the parameter is {Action}Request, the result {Action}Response, also inside a task")]
    [InlineData(nameof(SpecTarget.Subtract), true)]
    [InlineData(nameof(SpecTarget.Upper), true)]
    [InlineData(nameof(SpecTarget.Length), true)]
    [InlineData(nameof(SpecTarget.Echo), false)]
    [InlineData(nameof(SpecTarget.Reject), true)]
    [InlineData(nameof(SpecTarget.Mismatch), false)]
    public void NamingConventionPolicy_IsCallable_FollowsConvention(string methodName, bool expected)
    {
        var method = typeof(SpecTarget).GetMethod(methodName, [typeof(SpecTarget).GetMethod(methodName)!.GetParameters()[0].ParameterType])!;

        Assert.Equal(expected, new JsonRpcNamingConventionPolicy().IsCallable(method));
    }

    [Fact(DisplayName = "The method policy is asked about each method once, even when the first calls to a type arrive together")]
    public async Task DispatchAsync_ConcurrentFirstCalls_AskPolicyOncePerMethod()
    {
        var policy = new CountingPolicy();
        var dispatcher = DispatcherFixture.Create(o => o.MethodPolicy = policy);

        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
            CallAsync(dispatcher, "Spec.Subtract", """{"minuend": 1, "subtrahend": 1}"""))));

        Assert.NotEmpty(policy.Asked);
        Assert.All(policy.Asked.Values, count => Assert.Equal(1, count));
    }

    [Fact(DisplayName = "A method the policy throws for is not callable, and the other methods of the type still are")]
    public async Task DispatchAsync_PolicyThrowsForOneMethod_OnlyThatMethodIsRefused()
    {
        var dispatcher = DispatcherFixture.Create(o => o.MethodPolicy = new ThrowingForUpperPolicy());

        var upper = await CallAsync(dispatcher, "Spec.Upper", """{"text": "a"}""");
        var subtract = await CallAsync(dispatcher, "Spec.Subtract", """{"minuend": 5, "subtrahend": 3}""");

        Assert.Equal(JsonRpcErrorCodes.MethodNotFound, upper.Error!.Code);
        Assert.Equal(2, subtract.Result!.Value.GetProperty("difference").GetInt32());
    }

    private sealed class CountingPolicy : IJsonRpcMethodPolicy
    {
        public System.Collections.Concurrent.ConcurrentDictionary<string, int> Asked { get; } = new(StringComparer.Ordinal);

        public bool IsCallable(MethodInfo method)
        {
            Asked.AddOrUpdate(method.Name, 1, (_, count) => count + 1);
            Thread.Sleep(1);
            return true;
        }
    }

    private sealed class ThrowingForUpperPolicy : IJsonRpcMethodPolicy
    {
        public bool IsCallable(MethodInfo method) =>
            method.Name == nameof(SpecTarget.Upper) ? throw new InvalidOperationException("Policy bug") : true;
    }
}
