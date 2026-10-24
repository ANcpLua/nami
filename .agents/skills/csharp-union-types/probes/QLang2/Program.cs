#nullable enable
using System.Runtime.CompilerServices;

#if CLASS_PROVIDER
ClassProv cp = 42;
Console.WriteLine($"plain sealed class provider, private ctor(object?), Create => new(i): {cp is int}");
#endif
#if CAST_FIX
RecurseCast rcf = 42;
RecurseCast rcs = "hello";
Console.WriteLine($"record class provider, private ctor(object?), Create => new((object?)x): {rcf is int}, {rcs is string}");
#endif
#if REC_RECURSE
RecurseUnion r = 42;
Console.WriteLine($"RecurseUnion => {r}");
#else
StaticAbstractUnion s1 = 42;
StaticAbstractUnion s2 = "hello";
Console.WriteLine($"static abstract provider: {s1 is int}, {s2 is string}, default is null: {default(StaticAbstractUnion) is null}");
HasValueUnion h1 = "hello";
HasValueUnion h2 = default;
Console.WriteLine($"HasValue provider: {h1 is string}, default is null: {h2 is null}");
RecordClassUnion rc1 = 42;
RecordClassUnion rc2 = "hello";
Console.WriteLine($"record class provider (private ctor with extra param): {rc1 is int}, {rc2 is string}");
PartUnion pu = 42;
Console.WriteLine($"partial union: {pu.Extra()}; discard switch: {pu switch { _ => "discard" }}");
GenUnion<Payload> g = new Payload(1);
Console.WriteLine($"generic union with constraint + interface list, g is IMarker: {g is IMarker}");
OneOrMore<int> one = 5;
OneOrMore<int> many = new List<int> { 1, 2 };
Console.WriteLine($"overlapping cases: one={one.Value?.GetType().Name}, many={many.Value?.GetType().Name}");
#endif

// ---- missing-null-arm variants: expect NO warning ----
static string SwOption(Option<string> o) => o switch { None => "n", Some<string> => "s" };
static string SwIface(IfaceUnion u) => u switch { ICase => "i", OtherCase => "o" };
static string SwVal(IntOrBool2 v) => v switch { int => "i", bool => "b" };
static string SwStructField(StructHolder h) => h.F switch { int => "i", bool => "b" };
#nullable disable
static string SwDisabled(IntOrBool2 v) => v switch { int => "i", bool => "b" };
#nullable enable
// ---- canary: MUST produce CS8655 in this configuration ----
static string Canary(IntOrBool2 v) { _ = v is null; return v switch { int => "i", bool => "b" }; }

#if TEST_EVENT
public union EvUnion(int, string) { public event Action? E; }
#endif
#if TEST_CHAIN
static UnionTarget Chain(SourceVal s) => s;
#endif
#if TEST_EXPLICIT
static UnionInt Expl(ExplicitSrc s) => (UnionInt)s;
#endif
#if TEST_LIST
static bool List(OneOrMore<int> o) => o is [1, 2];
#endif
#if TEST_UNION_INTO_UNION
static UnionB IntoUnion(UnionA a) => a;
#endif

public record None;
public record Some<T>(T Value);
public union Option<T>(None, Some<T>);
public interface ICase;
public record CaseImpl : ICase;
public record OtherCase;
public union IfaceUnion(ICase, OtherCase);
public union IntOrBool2(int, bool);
public struct StructHolder { public IntOrBool2 F; }

public partial union PartUnion(int, string);
public partial union PartUnion { public int Extra() => 42; }
public interface IMarker;
public record Payload(int N);
public union GenUnion<T>(T, string) : IMarker where T : class;
public union OneOrMore<T>(T, IEnumerable<T>);

public record SourceVal(int N);
public record TargetVal(int N) { public static implicit operator TargetVal(SourceVal s) => new(s.N); }
public union UnionTarget(TargetVal, string);
public record ExplicitSrc(int N) { public static explicit operator int(ExplicitSrc s) => s.N; }
public union UnionInt(int, string);
public union UnionA(int, string);
public union UnionB(int, string, bool);

[Union]
public readonly struct StaticAbstractUnion : StaticAbstractUnion.IUnionMembers
{
    private readonly object? _value;
    private StaticAbstractUnion(object? value) => _value = value;
    public interface IUnionMembers
    {
        static abstract StaticAbstractUnion Create(string s);
        static abstract StaticAbstractUnion Create(int i);
        object? Value { get; }
    }
    public static StaticAbstractUnion Create(string s) => new(s);
    public static StaticAbstractUnion Create(int i) => new(i);
    object? IUnionMembers.Value => _value;
}

[Union]
public readonly struct HasValueUnion : HasValueUnion.IUnionMembers
{
    private readonly object? _value;
    private HasValueUnion(object? value) => _value = value;
    public interface IUnionMembers
    {
        static HasValueUnion Create(string s) => new(s);
        object? Value { get; }
        bool HasValue { get; }
    }
    object? IUnionMembers.Value => _value;
    public bool HasValue => _value != null;
}

[Union]
public record class RecordClassUnion : RecordClassUnion.IUnionMembers
{
    private readonly object? _value;
    private RecordClassUnion(object? value, bool _) => _value = value;
    public interface IUnionMembers
    {
        static RecordClassUnion Create(string s) => new(s, true);
        static RecordClassUnion Create(int i) => new(i, true);
        object? Value { get; }
    }
    object? IUnionMembers.Value => _value;
}

#if CLASS_PROVIDER
[Union]
public sealed class ClassProv : ClassProv.IUnionMembers
{
    private readonly object? _value;
    private ClassProv(object? value) => _value = value;
    public interface IUnionMembers
    {
        static ClassProv Create(string s) => new(s);
        static ClassProv Create(int i) => new(i);
        object? Value { get; }
    }
    object? IUnionMembers.Value => _value;
}
#endif

#if CAST_FIX
[Union]
public record class RecurseCast : RecurseCast.IUnionMembers
{
    private readonly object? _value;
    private RecurseCast(object? value) => _value = value;
    public interface IUnionMembers
    {
        static RecurseCast Create(string s) => new((object?)s);
        static RecurseCast Create(int i) => new((object?)i);
        object? Value { get; }
    }
    object? IUnionMembers.Value => _value;
}
#endif

#if REC_RECURSE
[Union]
public record class RecurseUnion : RecurseUnion.IUnionMembers
{
    private readonly object? _value;
    private RecurseUnion(object? value) => _value = value;
    public interface IUnionMembers
    {
        static RecurseUnion Create(string s) => new(s);
        static RecurseUnion Create(int i) => new(i);
        object? Value { get; }
    }
    object? IUnionMembers.Value => _value;
}
#endif
