using Nami.Tests.Infrastructure;

namespace Nami.IntegrationTests.Projects;

[TestClass]
public sealed class FixtureWorkspaceTests(TestContext context)
{
    [TestMethod]
    public async Task ArtifactPolicy_RegressionSuite_PassesThroughTheRepositoryGate()
    {
        var result = await ProcessRunner.RunAsync("python3",
            ["-m", "unittest", "discover", "-s", "eng/scripts/tests", "-p", "test_artifact_lifetime.py", "-v"],
            ProcessRunner.RepositoryRoot, context.CancellationToken);
        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        Assert.Contains("test_active_lease_cannot_override_hard_deadline", result.Error);
        Assert.Contains("test_old_implicit_retention_is_removed", result.Error);
        Assert.MatchesRegex(@"Ran [1-9][0-9]* tests", result.Error);
    }

    [TestMethod]
    public async Task DisposeAsync_MaterializedConsumer_RemovesItsWorkspace()
    {
        string directory;
        await using (var project = new FixtureProject("OwnedWorkspace"))
        {
            directory = project.DirectoryPath;
            project.AddSource("Example.cs", "public sealed class Example;").Materialize();
            Assert.IsTrue(File.Exists(project.ProjectPath));
        }

        Assert.IsFalse(Directory.Exists(directory));
    }

    [TestMethod]
    public async Task DisposeAsync_ExceptionDuringUse_RemovesItsWorkspace()
    {
        string? directory = null;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
        {
            await using var project = new FixtureProject("FailedWorkspace");
            directory = project.DirectoryPath;
            project.Materialize();
            throw new InvalidOperationException("Simulated test failure");
        });

        Assert.IsNotNull(directory);
        Assert.IsFalse(Directory.Exists(directory));
    }
}
