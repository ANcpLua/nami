using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace Nami.Tests.Policies;

/// <summary>
///     A telemetry obligation declared on a test method or class. <see cref="TracePerTestAttribute" /> verifies it
///     once the test has passed, against every trace that <see cref="TelemetryCapture" />
///     published during the invocation, and fails the test with the
///     violations and an evidence file. A policy without published evidence fails instead of passing vacuously.
/// </summary>
/// <remarks>A method declaration replaces the class declaration of the same policy.</remarks>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public abstract class TelemetryPolicyAttribute : Attribute
{
    private protected TelemetryPolicyAttribute()
    {
    }

    internal string Name => GetType().Name[..^nameof(Attribute).Length];

    internal IEnumerable<string> Check(TelemetryInvocation invocation)
    {
        return invocation.Traces.Count == 0
            ?
            [
                "No trace was published. Capture through TelemetryCapture.TraceAsync inside the test."
            ]
            : Violations(invocation);
    }

    private protected abstract IEnumerable<string> Violations(TelemetryInvocation invocation);

    private protected static string Describe(SpanRecord span)
    {
        return $"'{span.Name}' ({span.Kind}, {span.Service ?? "no service"}, {span.Scope?.Name ?? "no scope"})";
    }

    internal static IReadOnlyList<TelemetryPolicyAttribute> DeclaredOn(MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var declared = method.GetCustomAttributes<TelemetryPolicyAttribute>(true).ToList();
        var type = method.ReflectedType ?? method.DeclaringType;
        if (type is not null)
        {
            declared.AddRange(type.GetCustomAttributes<TelemetryPolicyAttribute>(true)
                .Where(policy => declared.TrueForAll(own => own.GetType() != policy.GetType())));
        }

        return declared.AsReadOnly();
    }

    /// <summary>Fails a publication from a plain test class whose policies would otherwise never run.</summary>
    internal static void RejectUnverifiedPolicies()
    {
#pragma warning disable MSTESTEXP
        var context = TestContext.Current;
#pragma warning restore MSTESTEXP
        string? name;
        try
        {
            name = context?.FullyQualifiedTestClassName;
        }
        catch (InvalidOperationException)
        {
            return;
        }

        if (name is not null) RejectUnverifiedPolicies(name);
    }

    internal static void RejectUnverifiedPolicies(string testClassName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testClassName);
        if (!RuntimeFeature.IsDynamicCodeSupported)
        {
            throw new InvalidOperationException(
                "Telemetry published outside a [TracePerTest] invocation cannot verify policies in a NativeAOT test runner. Use [TracePerTest] for telemetry publication.");
        }

        RejectManagedUnverifiedPolicies(testClassName);
    }

    [RequiresUnreferencedCode(
        "The plain-test policy guard resolves the running test class by name and inspects its policy attributes. It requires an untrimmed managed test process.")]
    private static void RejectManagedUnverifiedPolicies(string name)
    {
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name, false)).OfType<Type>().FirstOrDefault();
        if (type is null)
        {
            throw new InvalidOperationException(
                $"Cannot verify telemetry policies because the active test class '{name}' could not be resolved. Use [TracePerTest] for telemetry publication.");
        }

        if (type.IsDefined(typeof(TracePerTestAttribute), false)) return;
        var policies = type.GetCustomAttributes<TelemetryPolicyAttribute>(true)
            .Concat(type.GetMethods().SelectMany(test => test.GetCustomAttributes<TelemetryPolicyAttribute>(true)))
            .Select(policy => policy.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (policies.Length > 0)
        {
            throw new InvalidOperationException($"{name} declares [{string.Join("], [", policies)}], but only a "
                                                + "[TracePerTest] class verifies telemetry policies. Replace [TestClass] with [TracePerTest].");
        }
    }
}
