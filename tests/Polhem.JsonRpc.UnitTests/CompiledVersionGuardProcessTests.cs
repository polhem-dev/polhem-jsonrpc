using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Polhem.JsonRpc.Server;

namespace Polhem.JsonRpc.UnitTests;

/// <summary>
/// Loads an assembly compiled against <c>Polhem.JsonRpc.Server</c> 1.0 into the test process and checks the public
/// constructors, which scan the loaded assemblies themselves.
/// </summary>
/// <remarks>
/// While the assembly is loaded every dispatcher of the process refuses to start, so these tests run alone, after the
/// parallel ones, and unload it before they return.
/// </remarks>
[Collection(Name)]
public class CompiledVersionGuardProcessTests
{
    public const string Name = "Loads a stale assembly";

    private const string StaleName = "Polhem.JsonRpc.UnitTests.Stale";

    [Fact(DisplayName = "Version guard: both public constructors refuse to start once an assembly compiled against Server 1.0 is loaded, and start again once it is gone")]
    public void PublicConstructors_StaleAssemblyLoaded_Throw()
    {
        var options = new JsonRpcServerOptions { ObjectFactory = new TestObjectFactory() };
        _ = new JsonRpcDispatcher(options);

        var context = LoadStale();
        try
        {
            var single = Assert.Throws<InvalidOperationException>(() => new JsonRpcDispatcher(options));
            var withFactory = Assert.Throws<InvalidOperationException>(() => new JsonRpcDispatcher(options, new TestObjectFactory()));

            Assert.Contains($"{StaleName} 1.0.0.0", single.Message, StringComparison.Ordinal);
            Assert.Contains($"{StaleName} 1.0.0.0", withFactory.Message, StringComparison.Ordinal);
        }
        finally
        {
            Unload(context);
        }

        _ = new JsonRpcDispatcher(options);
    }

    // Kept out of the test method so that nothing on its stack holds the assembly when the context is unloaded.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LoadStale()
    {
        var context = new AssemblyLoadContext(StaleName, isCollectible: true);
        using var image = new MemoryStream(BuildStaleAssembly());
        context.LoadFromStream(image);
        return new WeakReference(context);
    }

    private static void Unload(WeakReference context)
    {
        StartUnload(context);
        for (var attempt = 0; context.IsAlive && attempt < 20; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        // A context still alive would leave every later dispatcher of the run refusing to start.
        Assert.False(context.IsAlive, "The stale assembly could not be unloaded.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StartUnload(WeakReference context) => ((AssemblyLoadContext)context.Target!).Unload();

    // An empty library whose only reference is Polhem.JsonRpc.Server 1.0.0.0, as a package compiled against 1.0 has.
    private static byte[] BuildStaleAssembly()
    {
        var metadata = new MetadataBuilder();
        metadata.AddAssembly(metadata.GetOrAddString(StaleName), new Version(1, 0, 0, 0), default, default, default,
            AssemblyHashAlgorithm.Sha1);
        metadata.AddModule(0, metadata.GetOrAddString(StaleName + ".dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssemblyReference(metadata.GetOrAddString("Polhem.JsonRpc.Server"), new Version(1, 0, 0, 0), default,
            default, default, default);
        metadata.AddTypeDefinition(default, default, metadata.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));

        var image = new BlobBuilder();
        new ManagedPEBuilder(PEHeaderBuilder.CreateLibraryHeader(), new MetadataRootBuilder(metadata), new BlobBuilder())
            .Serialize(image);
        return image.ToArray();
    }
}

[CollectionDefinition(CompiledVersionGuardProcessTests.Name, DisableParallelization = true)]
public class CompiledVersionGuardProcessCollection;
