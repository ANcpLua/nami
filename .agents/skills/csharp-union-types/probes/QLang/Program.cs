#nullable enable
using System.Runtime.CompilerServices;

FourEyesApproval real = new PartlyApproved(Guid.NewGuid());
FourEyesApproval dflt = default;

Console.WriteLine("--- Trap 7 ---");
#if RC1_IS_UNION_TYPE // CS8121 from RC 2 on
Console.WriteLine($"real is FourEyesApproval: {real is FourEyesApproval}");
Console.WriteLine($"dflt is FourEyesApproval: {dflt is FourEyesApproval}");
#endif
Console.WriteLine($"dflt is null: {dflt is null}; real is null: {real is null}");
Console.WriteLine($"dflt is not null: {dflt is not null}; real is not null: {real is not null}");
Console.WriteLine($"real is PartlyApproved {{ Approver: _ }}: {real is PartlyApproved { Approver: _ }}");
IntOrString fifteen = 15, none = default;
Console.WriteLine($"15 is > 10: {fifteen is > 10}; default is > 10: {none is > 10}; 15 is 15: {fifteen is 15}");
if (dflt is var v) Console.WriteLine($"is var captures: {v.GetType().Name}");
Console.WriteLine($"not null and var x => {NotNullAndVar(real)}");

Console.WriteLine("--- Trap 1 runtime ---");
Console.WriteLine($"NoNullArm(real) => {NoNullArm(real)}");
try { Console.WriteLine($"NoNullArm(default) => {NoNullArm(dflt)}"); }
catch (Exception e) { Console.WriteLine($"NoNullArm(default) THROWS {e.GetType().Name}"); }
Console.WriteLine($"WithNullArm(default) => {WithNullArm(dflt)}");

Console.WriteLine("--- conversions ---");
Temp t = new Celsius(100);
Console.WriteLine($"Temp t = new Celsius(100) => Value is {t.Value}");

Console.WriteLine("--- escape hatches (doc samples) ---");
Outcome<int> ok = 42;
Outcome<int> err = new InvalidOperationException("boom");
Console.WriteLine($"Outcome<int> ok => {Describe(ok)}; err => {Describe(err)}; default => {Describe(default)}");
IntOrBool ib = new IntOrBool((int?)42);
Console.WriteLine($"IntOrBool => {ib switch { int i => $"int: {i}", bool bb => $"bool: {bb}" }}");
Result<string> rok = new Result<string>("success");
Result<string>? rnull = null;
Result<string> rempty = new Result<string>((string?)null);
Console.WriteLine($"Result: {rok switch { string s => $"OK: {s}", Exception e => $"Error: {e.Message}", null => "null" }}; null ref is null: {rnull is null}; null Value is null: {rempty is null}");
Shape shape = new Shape(new Circle(5.0));
Console.WriteLine($"Shape area: {shape switch { Circle c => Math.PI * c.Radius * c.Radius, Rectangle r => r.Width * r.Height }:F2}");
Console.WriteLine($"Length: {new Length(new Feet(10)).Add(new Meters(1)).TotalMeters:F3}");

#if PROBE_ERR2
bool e1 = real is { Approver: _ };
#endif

static string NoNullArm(FourEyesApproval a) => a switch        // PROBE: no null arm
{
    NotRequired => "nr", PendingApproval => "p", PartlyApproved => "pa", FullyApproved => "fa", Rejected => "r",
};
static string WithNullArm(FourEyesApproval a) => a switch
{
    NotRequired => "nr", PendingApproval => "p", PartlyApproved => "pa", FullyApproved => "fa", Rejected => "r", null => "null arm",
};
static string MissingCaseWithNullArm(FourEyesApproval a) => a switch   // PROBE: Rejected missing, null arm present
{
    NotRequired => "nr", PendingApproval => "p", PartlyApproved => "pa", FullyApproved => "fa", null => "n",
};
static string NullableCaseNoNullArm(MaybeText m) => m switch     // PROBE: a nullable case type, no null arm
{
    string s => s, int i => i.ToString(),
};
static string FlowAssigned()                                     // PROBE: local assigned from a case, no null arm
{
    FourEyesApproval a = new Rejected(Guid.Empty);
    return a switch { NotRequired => "nr", PendingApproval => "p", PartlyApproved => "pa", FullyApproved => "fa", Rejected => "r" };
}
static string LocalDefault()                                     // PROBE: local assigned default, no null arm
{
    FourEyesApproval a = default;
    return a switch { NotRequired => "nr", PendingApproval => "p", PartlyApproved => "pa", FullyApproved => "fa", Rejected => "r" };
}
static string ArraySlot()                                        // PROBE: array slot, no null arm
{
    var arr = new FourEyesApproval[1];
    return arr[0] switch { NotRequired => "nr", PendingApproval => "p", PartlyApproved => "pa", FullyApproved => "fa", Rejected => "r" };
}
static string HolderField(Holder h) => h.A switch                // PROBE: never-assigned field, no null arm
{
    NotRequired => "nr", PendingApproval => "p", PartlyApproved => "pa", FullyApproved => "fa", Rejected => "r",
};
static string AfterNullTest(FourEyesApproval a)                  // PROBE: after `is null` test, no null arm
{
    _ = a is null ? 1 : 0;
    return a switch { NotRequired => "nr", PendingApproval => "p", PartlyApproved => "pa", FullyApproved => "fa", Rejected => "r" };
}
static object NotNullAndVar(FourEyesApproval a) => a switch { not null and var x => x.GetType().Name, _ => "none" };
static string Describe(Outcome<int> o) => o switch { int i => $"int {i}", Exception e => $"ex {e.Message}", null => "null" };

public union FourEyesApproval(NotRequired, PendingApproval, PartlyApproved, FullyApproved, Rejected);
public record class NotRequired;
public record class PendingApproval;
public record class PartlyApproved(Guid Approver);
public record class FullyApproved(Guid Approver1, Guid Approver2);
public record class Rejected(Guid Rejector);
public union IntOrString(int, string);
public union MaybeText(string?, int);
public class Holder { public FourEyesApproval A; }

public record class Celsius(double V);
public record class Fahrenheit(double V);
public union Temp(Celsius, Fahrenheit)
{
    public static implicit operator Temp(Celsius c) => new Fahrenheit(c.V * 9 / 5 + 32);
}

#if PROBE_ERR
public union BadField(Celsius) { private int _f; }
public union BadAutoProp(Celsius) { public int P { get; set; } }
public union BadSingleCtor(Celsius, Fahrenheit) { public BadSingleCtor(double d) : this(new Celsius(d)) { } }
#endif
#if PROBE_ERR2
public union BadNoDelegate(Celsius, Fahrenheit) { public BadNoDelegate(double a, double b) { } }
#endif

// ---- verbatim from the C# language reference, Union types (ms.date 2026-08-14) ----
[System.Runtime.CompilerServices.Union]
public struct Outcome<T> : Outcome<T>.IUnionMembers
{
    private readonly object? _value;

    private Outcome(object? value) => _value = value;

    public interface IUnionMembers
    {
        static Outcome<T> Create(T? value) => new(value);
        static Outcome<T> Create(Exception? value) => new(value);
        object? Value { get; }

        // Optional but recommended: TryGetValue enables efficient pattern matching
        bool TryGetValue(out T value);
        bool TryGetValue(out Exception value);
    }

    object? IUnionMembers.Value => _value;

    public bool TryGetValue(out T value)
    {
        if (_value is T t)
        {
            value = t;
            return true;
        }
        value = default!;
        return false;
    }

    public bool TryGetValue(out Exception value)
    {
        if (_value is Exception e)
        {
            value = e;
            return true;
        }
        value = default!;
        return false;
    }
}

[System.Runtime.CompilerServices.Union]
public struct IntOrBool : System.Runtime.CompilerServices.IUnion
{
    private readonly int _intValue;
    private readonly bool _boolValue;
    private readonly byte _tag; // 0 = none, 1 = int, 2 = bool

    public IntOrBool(int? value)
    {
        if (value.HasValue)
        {
            _intValue = value.Value;
            _tag = 1;
        }
    }

    public IntOrBool(bool? value)
    {
        if (value.HasValue)
        {
            _boolValue = value.Value;
            _tag = 2;
        }
    }

    public object? Value => _tag switch
    {
        1 => _intValue,
        2 => _boolValue,
        _ => null
    };

    public bool HasValue => _tag != 0;

    public bool TryGetValue(out int value)
    {
        value = _intValue;
        return _tag == 1;
    }

    public bool TryGetValue(out bool value)
    {
        value = _boolValue;
        return _tag == 2;
    }
}

[System.Runtime.CompilerServices.Union]
public class Result<T> : System.Runtime.CompilerServices.IUnion
{
    private readonly object? _value;

    public Result(T? value) { _value = value; }
    public Result(Exception? value) { _value = value; }

    public object? Value => _value;
}

[System.Runtime.CompilerServices.Union]
public struct Shape : System.Runtime.CompilerServices.IUnion
{
    private readonly object? _value;

    public Shape(Circle value) { _value = value; }
    public Shape(Rectangle value) { _value = value; }

    public object? Value => _value;
}

public record class Circle(double Radius);
public record class Rectangle(double Width, double Height);

public record class Meters(double Value);
public record class Feet(double Value);

public union Length(Meters, Feet)
{
    public double TotalMeters => this switch
    {
        Meters m => m.Value,
        Feet f => f.Value * 0.3048,
        _ => throw new InvalidOperationException("The Length has no value."),
    };

    public Length Add(Length other) => new Meters(TotalMeters + other.TotalMeters);
}
