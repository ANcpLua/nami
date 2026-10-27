using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace Nami.Tests;

/// <summary>Declares the sources and dependencies of an isolated consumer project.</summary>
[PublicAPI]
public interface IProjectFixture
{
    static abstract string Name { get; }
    static abstract void Configure(FixtureProject project);
}

/// <summary>Provides one isolated project for every test invocation and data row.</summary>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class UsesFixtureAttribute<TFixture> : TestClassAttribute where TFixture : IProjectFixture
{
    public override TestMethodAttribute GetTestMethodAttribute(TestMethodAttribute testMethodAttribute)
    {
        ArgumentNullException.ThrowIfNull(testMethodAttribute);
        return new FixtureTestMethodAttribute<TFixture>(testMethodAttribute);
    }
}

/// <summary>The project owned by the current attributed test invocation.</summary>
[PublicAPI]
public static class FixtureContext
{
    public static FixtureProject Current<TFixture>() where TFixture : IProjectFixture
    {
        return FixtureInvocation.Get(typeof(TFixture))
               ?? throw new InvalidOperationException(
                   $"No {typeof(TFixture).Name} fixture is active for this invocation.");
    }
}

internal static class FixtureInvocation
{
    private static readonly AsyncLocal<ImmutableDictionary<Type, FixtureProject>?> s_current = new();

    internal static FixtureProject? Get(Type fixture)
    {
        return s_current.Value?.GetValueOrDefault(fixture);
    }

    internal static IDisposable Push(Type fixture, FixtureProject project)
    {
        var previous = s_current.Value;
        s_current.Value = (previous ?? ImmutableDictionary<Type, FixtureProject>.Empty).SetItem(fixture, project);
        return new Activation(previous);
    }

    private sealed class Activation(ImmutableDictionary<Type, FixtureProject>? previous) : IDisposable
    {
        public void Dispose()
        {
            s_current.Value = previous;
        }
    }
}

/// <summary>The Capture source set, compiled into a consumer without referencing the test assembly.</summary>
[PublicAPI]
public sealed class CaptureFixture : IProjectFixture
{
    private CaptureFixture()
    {
    }

    public static string Name => nameof(CaptureFixture);

    public static void Configure(FixtureProject project)
    {
        project.AddCaptureSources();
    }
}

internal sealed class FixtureTestMethodAttribute<TFixture>(
    TestMethodAttribute inner,
    [CallerFilePath] string file = "",
    [CallerLineNumber] int line = -1) : TestMethodAttribute(file, line)
    where TFixture : IProjectFixture
{
    public override async Task<TestResult[]> ExecuteAsync(ITestMethod testMethod)
    {
        ArgumentNullException.ThrowIfNull(testMethod);
#pragma warning disable MSTESTEXP
        var context = TestContext.Current
                      ?? throw new InvalidOperationException("MSTest did not provide the fixture invocation context.");
#pragma warning restore MSTESTEXP
        await using var project = new FixtureProject(TFixture.Name, context: context);
        TFixture.Configure(project);
        using var activation = FixtureInvocation.Push(typeof(TFixture), project);
        var results = await inner.ExecuteAsync(testMethod).ConfigureAwait(false);
        var files = project.EvidenceFiles.ToArray();
        foreach (var result in results)
            result.ResultFiles = [.. (result.ResultFiles ?? []).Concat(files).Distinct(StringComparer.Ordinal)];
        return results;
    }
}
