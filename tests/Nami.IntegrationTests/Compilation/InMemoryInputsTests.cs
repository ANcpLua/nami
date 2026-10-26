using Microsoft.CodeAnalysis.CSharp;
using Nami.Tests.Compilation;

namespace Nami.IntegrationTests.Compilation;

[TestClass]
[TestCategory("Compiler")]
public sealed class InMemoryInputsTests(TestContext context)
{
    [TestMethod]
    public void GetText_VirtualFile_PreservesPathAndContent()
    {
        var file = new InMemoryAdditionalText("/virtual/BannedSymbols.txt", "T:Example.Blocked");

        Assert.AreEqual("/virtual/BannedSymbols.txt", file.Path);
        Assert.AreEqual("T:Example.Blocked", file.GetText(context.CancellationToken).ToString());
    }

    [TestMethod]
    public void GetText_CancelledToken_ThrowsBeforeReading()
    {
        var file = new InMemoryAdditionalText("/virtual/data.txt", "data");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var token = cancellation.Token;

        _ = Assert.ThrowsExactly<OperationCanceledException>(() => file.GetText(token));
    }

    [TestMethod]
    public void Options_SourceDictionaryMutates_RetainsOriginalValues()
    {
        var input = new Dictionary<string, string> { ["fixture.option"] = "original" };
        var options = new InMemoryAnalyzerOptions(input);
        input["fixture.option"] = "changed";

        Assert.IsTrue(options.GlobalOptions.TryGetValue("FIXTURE.OPTION", out var value));
        Assert.AreEqual("original", value);
    }

    [TestMethod]
    public void GetOptions_MatchingVirtualPath_ReturnsOnlyFileOptions()
    {
        var options = new InMemoryAnalyzerOptions(files: new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["/virtual/One.cs"] = new Dictionary<string, string> { ["fixture.option"] = "one" }
        });
        var first = CSharpSyntaxTree.ParseText("", path: "/virtual/One.cs",
            cancellationToken: context.CancellationToken);
        var second =
            CSharpSyntaxTree.ParseText("", path: "/virtual/Two.cs", cancellationToken: context.CancellationToken);

        Assert.IsTrue(options.GetOptions(first).TryGetValue("fixture.option", out var value));
        Assert.AreEqual("one", value);
        Assert.IsFalse(options.GetOptions(second).TryGetValue("fixture.option", out _));
    }
}
