namespace Nami.UnitTests.Collector;

[TestClass]
[TestCategory("Fast")]
public class FixtureProjectTests
{
    [TestMethod]
    public void Should_KeepFrameworkModesExclusive_When_SwitchingBackToOneFramework()
    {
        var definition = new FixtureProjectDefinition("Consumer", "net10.0")
            .WithTargetFrameworks("net9.0", "net10.0");
        Assert.IsFalse(definition.Properties.ContainsKey("TargetFramework"));

        definition.WithProperty("TargetFramework", "net10.0");

        Assert.AreEqual("net10.0", definition.Properties["TargetFramework"]);
        Assert.IsFalse(definition.Properties.ContainsKey("TargetFrameworks"));
    }

    [TestMethod]
    public void Should_SnapshotCustomTargets_When_TheOriginalTargetIsChanged()
    {
        var target = new FixtureTarget("Check", "CoreCompile").Warning("CHECK001", "<original> & value");
        var definition = new FixtureProjectDefinition("Consumer", "net10.0").WithCustomTarget(target);
        target.Error("CHECK002", "later change");

        var emitted = Assert.ContainsSingle(definition.ToProjectDocument().Descendants("Target"));

        Assert.AreEqual("CoreCompile", (string?)emitted.Attribute("BeforeTargets"));
        Assert.IsEmpty(emitted.Elements("Error"));
        Assert.AreEqual("<original> & value",
            (string?)Assert.ContainsSingle(emitted.Elements("Warning")).Attribute("Text"));
    }

    [TestMethod]
    public void Should_DeduplicatePropertyRequests_When_ANameIsRecordedTwice()
    {
        var definition = new FixtureProjectDefinition("Consumer", "net10.0")
            .RecordProperties("Flavor", "Flavor");

        Assert.HasCount(1, definition.ToProjectDocument().Descendants("_FixtureProperty"));
        var writes = definition.ToProjectDocument().Descendants("WriteLinesToFile").ToArray();
        Assert.HasCount(2, writes);
        Assert.IsTrue(writes.All(write =>
            ((string?)write.Attribute("File"))?.Contains("$(TargetFramework)", StringComparison.Ordinal) == true));
    }

    [TestMethod]
    public void Should_DescribeAnOrdinaryExecutable_When_TheDefinitionIsCreated()
    {
        var definition = new FixtureProjectDefinition("MemoryConsumer", "net10.0");

        var project = definition.ToProjectDocument().Root!;

        Assert.AreEqual("Microsoft.NET.Sdk", (string?)project.Attribute("Sdk"));
        Assert.AreEqual("net10.0", (string?)project.Element("PropertyGroup")!.Element("TargetFramework"));
        Assert.AreEqual("Exe", (string?)project.Element("PropertyGroup")!.Element("OutputType"));
    }

    [TestMethod]
    public void Should_KeepVersionsInTheCentralDocument_When_APackageIsDeclared()
    {
        var definition = new FixtureProjectDefinition("MemoryConsumer", "net10.0")
            .AddPackage("Example.Dependency", "1.2.3");

        var reference = Assert.ContainsSingle(definition.ToProjectDocument().Descendants("PackageReference"));
        var version = Assert.ContainsSingle(definition.ToPackageDocument().Descendants("PackageVersion"));

        Assert.AreEqual("Example.Dependency", (string?)reference.Attribute("Include"));
        Assert.IsNull(reference.Attribute("Version"));
        Assert.AreEqual("Example.Dependency", (string?)version.Attribute("Include"));
        Assert.AreEqual("1.2.3", (string?)version.Attribute("Version"));
    }

    [TestMethod]
    public void Should_KeepTheDefinitionUnchanged_When_AProjectSnapshotIsEdited()
    {
        var definition = new FixtureProjectDefinition("MemoryConsumer", "net10.0")
            .WithProperty("Description", "original");
        var snapshot = definition.ToProjectDocument();
        snapshot.Root!.Element("PropertyGroup")!.Element("Description")!.Value = "edited";

        Assert.AreEqual("original",
            (string?)definition.ToProjectDocument().Root!.Element("PropertyGroup")!.Element("Description"));
    }

    [TestMethod]
    public void Should_RetainSourceText_When_ItIsAddedToTheMemoryDefinition()
    {
        const string Source = "Console.Write(\"memory-only\");";
        var definition = new FixtureProjectDefinition("MemoryConsumer", "net10.0")
            .AddSource("Nested/Program.cs", Source);

        var file = Assert.ContainsSingle(definition.Sources);

        Assert.AreEqual("Nested/Program.cs", file.Key);
        Assert.AreEqual(Source, file.Value);
    }

    [TestMethod]
    [DataRow("../Escape.cs")]
    [DataRow("nested/../../Escape.cs")]
    [DataRow(@"nested\..\Escape.cs")]
    [DataRow("/absolute.cs")]
    [DataRow(@"C:\absolute.cs")]
    [DataRow("source.txt")]
    public void Should_RejectUnsafeSourcePaths_When_MaterializingOutsideTheProject(string path)
    {
        var error = Assert.ThrowsExactly<ArgumentException>(() => FixtureProjectDefinition.NormalizeSourcePath(path));
        Assert.AreEqual("path", error.ParamName);
    }

    [TestMethod]
    [DataRow("Program.cs", "Program.cs")]
    [DataRow("Nested/Source.cs", "Nested/Source.cs")]
    [DataRow(@"Nested\Source.cs", "Nested/Source.cs")]
    public void Should_NormalizeRelativeCSharpSources_When_ThePathStaysInsideTheProject(string path, string expected)
    {
        Assert.AreEqual(expected, FixtureProjectDefinition.NormalizeSourcePath(path));
    }
}
