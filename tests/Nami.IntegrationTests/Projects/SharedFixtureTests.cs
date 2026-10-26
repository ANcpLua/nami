namespace Nami.IntegrationTests.Projects;

[TestClass]
public sealed class SharedFixtureTests(TestContext context)
{
    private static SharedFixture<FixtureProject>? s_fixture;
    private static int s_created;
    private static string? s_directory;

    [ClassInitialize]
    public static void Initialize(TestContext _)
    {
        s_created = 0;
        s_fixture = new SharedFixture<FixtureProject>(async () =>
        {
            var project = new FixtureProject("SharedInput");
            try
            {
                project.AddSource("Input.cs", "public sealed class SharedInput;").Materialize();
                s_directory = project.DirectoryPath;
                Interlocked.Increment(ref s_created);
                return project;
            }
            catch
            {
                await project.DisposeAsync();
                throw;
            }
        });
    }

    [ClassCleanup]
    public static async Task Cleanup()
    {
        Assert.IsNotNull(s_fixture);
        await s_fixture.DisposeAsync();
        Assert.AreEqual(1, s_created);
        Assert.IsNotNull(s_directory);
        Assert.IsFalse(Directory.Exists(s_directory));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    [DataRow(8)]
    [DataRow(9)]
    [DataRow(10)]
    public async Task GetAsync_TenTests_ReuseOneMaterializedFile(int invocation)
    {
        Assert.IsGreaterThan(0, invocation);
        Assert.IsNotNull(s_fixture);
        var project = await s_fixture.GetAsync(context.CancellationToken);
        Assert.AreSame(project, await s_fixture.GetAsync(context.CancellationToken));
        Assert.AreEqual(1, s_created);
        Assert.AreEqual(s_directory, project.DirectoryPath);
        Assert.AreEqual("public sealed class SharedInput;",
            await File.ReadAllTextAsync(Path.Combine(project.DirectoryPath, "Input.cs"), context.CancellationToken));
    }
}
