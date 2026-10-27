using Nami.IntegrationTests.Compilation;
using Nami.Tests.Compilation;
using Nami.Tests.Metadata;

namespace Nami.IntegrationTests.BinaryInspection;

[TestClass]
[TestCategory("Compiler")]
public sealed class PeImageTests(TestContext context)
{
    [TestMethod]
    public void ReadAssemblyMetadata_DuplicateKeys_PreservesAllValues()
    {
        var compiled = Compile("""
                               [assembly: System.Reflection.AssemblyMetadata("key", "first")]
                               [assembly: System.Reflection.AssemblyMetadata("key", null)]
                               public sealed class Model { }
                               """);
        using var image = new PeImage(compiled.Image);

        Assert.AreSequenceEqual([new AssemblyMetadataEntry("key", "first"), new AssemblyMetadataEntry("key", null)],
            image.ReadAssemblyMetadata());
    }

    [TestMethod]
    public void ReadAssemblyMetadata_ImpostorNamespace_IgnoresLookalikeAttributes()
    {
        var compiled = Compile("""
                               [assembly: Impostor.AssemblyMetadata("fake", "value")]
                               [assembly: System.Reflection.AssemblyMetadata("real", "value")]
                               namespace Impostor
                               {
                                   [System.AttributeUsage(System.AttributeTargets.Assembly)]
                                   public sealed class AssemblyMetadataAttribute(string key, string value) : System.Attribute
                                   {
                                       public string Key { get; } = key;
                                       public string Value { get; } = value;
                                   }
                               }
                               """);
        using var image = new PeImage(compiled.Image);

        Assert.AreEqual(new AssemblyMetadataEntry("real", "value"),
            Assert.ContainsSingle(image.ReadAssemblyMetadata()));
    }

    [TestMethod]
    public void ReadTargetFrameworkName_DeclaredAttribute_ReturnsFramework()
    {
        var compiled = Compile("""
                               [assembly: System.Runtime.Versioning.TargetFramework(".NETCoreApp,Version=v11.0")]
                               public sealed class Model { }
                               """);
        using var image = new PeImage(compiled.Image);

        Assert.AreEqual(".NETCoreApp,Version=v11.0", image.ReadTargetFrameworkName());
    }

    [TestMethod]
    public void ReadTargetFrameworkName_MissingAttribute_ReturnsNull()
    {
        using var image = new PeImage(Compile("public sealed class Model { }").Image);

        Assert.IsNull(image.ReadTargetFrameworkName());
    }

    [TestMethod]
    public void ReadAssemblyReferences_CollectionsType_ReturnsFrameworkVersion()
    {
        using var image =
            new PeImage(
                Compile(
                        "public sealed class Model { public System.Collections.Generic.List<int> Values { get; } = new(); }")
                    .Image);

        var reference =
            Assert.ContainsSingle(image.ReadAssemblyReferences().Where(item => item.Name == "System.Collections"));

        Assert.AreEqual(new Version(11, 0, 0, 0), reference.Version);
    }

    [TestMethod]
    public void HasCodeViewDebugEntry_PortableSymbols_ReturnsTrue()
    {
        using var image = new PeImage(Compile("public sealed class Model { }").Image);

        Assert.IsTrue(image.HasCodeViewDebugEntry());
    }

    [TestMethod]
    public void Dispose_CallerOwnedStream_LeavesStreamReadable()
    {
        using var stream = new MemoryStream([.. Compile("public sealed class Model { }").Image]);
        var image = new PeImage(stream);
        _ = image.ReadAssemblyReferences();

        image.Dispose();

        Assert.IsTrue(stream.CanRead);
        stream.Position = 0;
        Assert.AreEqual('M', stream.ReadByte());
    }

    [TestMethod]
    public void ReadAssemblyMetadata_InvalidImage_ThrowsBadImageFormat()
    {
        _ = Assert.ThrowsExactly<BadImageFormatException>(ReadInvalidImage);
    }

    private static void ReadInvalidImage()
    {
        using var image = new PeImage([1, 2, 3]);
        _ = image.ReadAssemblyMetadata();
    }

    private InProcessCompilationResult Compile(string source)
    {
        var project = new FixtureProjectDefinition("MetadataFixture", "net11.0")
            .WithProperty("OutputType", "Library").AddSource("Source.cs", source);
        return InProcessCompiler.Compile(project, CompilerReferences.Framework, cancellationToken: context.CancellationToken);
    }
}
