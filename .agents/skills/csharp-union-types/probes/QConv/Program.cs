// Conversions into a union: overlapping cases (the "ambiguous conversion" claim) and the
// target-typing / inference contexts a service returning a union meets.
// Builds that fail by design, one define each (dotnet build --no-incremental -p:DefineConstants=NAME):
//   AMBIG_SIBLINGS, NULL_LITERAL, INFER_TASK, VAR_TERNARY, TASK_CASE_TO_TASK_UNION, ENUMERABLE_COVARIANCE,
//   INFER_TWO_ARGS, NULLABLE_UNION_FROM_CASE_DEFAULT
using System.Collections;

Console.WriteLine("=== 1. Overlapping cases ===");
{
    L a = new List<int>();            Console.WriteLine($"union L(IEnumerable<int>, IList<int>) = List<int>   -> {Show(a)}");
    L b = new int[0];                 Console.WriteLine($"union L = int[]                                      -> {Show(b)}");
    IEnumerable<int> e = new List<int>();
    L c = new L(e);                   Console.WriteLine($"union L = IEnumerable<int> typed value               -> {Show(c)}");
    BD d = new Derived();             Console.WriteLine($"union BD(Base, Derived) = Derived                    -> {Show(d)}");
    BD f = new Base();                Console.WriteLine($"union BD = Base                                      -> {Show(f)}");
    Base viaBase = new Derived();
    BD g = viaBase;                   Console.WriteLine($"union BD = (Base)Derived                             -> {Show(g)}");
    DB h = new Derived();             Console.WriteLine($"union DB(Derived, Base) = Derived (order swapped)    -> {Show(h)}");
    LD i = 5;                         Console.WriteLine($"union LD(long, double) = int 5                       -> {Show(i)}");
    LD j = 5L;                        Console.WriteLine($"union LD = long 5                                    -> {Show(j)}");
    LD k = 5.0f;                      Console.WriteLine($"union LD = float                                     -> {Show(k)}");
    OS l = "x";                       Console.WriteLine($"union OS(object, string) = \"x\"                       -> {Show(l)}");
    OS m = 42;                        Console.WriteLine($"union OS = 42                                        -> {Show(m)}");
    Both bothImpl = new Both();
#if AMBIG_SIBLINGS
    Sib s = bothImpl;                 // class implements IFoo and IBar; union Sib(IFoo, IBar)
#else
    Sib s = new Sib((IFoo)bothImpl);  Console.WriteLine($"union Sib(IFoo, IBar) via explicit constructor        -> {Show(s)}");
#endif
#if NULL_LITERAL
    Sib n = null;
#endif
    Console.WriteLine($"overlap in the declaration compiles: OneOrMore<int>(int, IEnumerable<int>) = {Show((OneOrMore<int>)1)}");
}

Console.WriteLine();
Console.WriteLine("=== 3. Target typing and inference ===");
{
    bool flag = args.Length == 0;
    U t1 = flag ? new A() : new B();                 Console.WriteLine($"cond ? new A() : new B() -> U          : {Show(t1)}");
    U t2 = args.Length switch { 0 => new A(), _ => new B() };
                                                     Console.WriteLine($"switch arms A / B        -> U          : {Show(t2)}");
    Func<U> t3 = () => new A();                      Console.WriteLine($"Func<U> = () => new A()                : {Show(t3())}");
    Func<U> t3b = () => flag ? new A() : new B();    Console.WriteLine($"Func<U> = () => cond ? A : B           : {Show(t3b())}");
    U[] t4 = [new A(), new B()];                     Console.WriteLine($"U[] = [new A(), new B()]               : {string.Join(",", t4.Select(x => Show(x)))}");
    List<U> t5 = [new A()]; t5.Add(new B());         Console.WriteLine($"List<U> collection expr + Add(new B()) : {string.Join(",", t5.Select(x => Show(x)))}");
    U t6 = Id(new A());                              Console.WriteLine($"Id(new A()) assigned to U (T=A)        : {Show(t6)}");
    U t7 = Id<U>(new A());                           Console.WriteLine($"Id<U>(new A())                         : {Show(t7)}");
    Task<U> t8 = Task.FromResult<U>(new A());        Console.WriteLine($"Task.FromResult<U>(new A())            : {Show(await t8)}");
    Task<U> t9 = AsyncReturn(true);                  Console.WriteLine($"async Task<U> return new A()           : {Show(await t9)}");
    ValueTask<U> t10 = AsyncValueReturn();           Console.WriteLine($"async ValueTask<U> return new B()      : {Show(await t10)}");
    U? t11 = new A();                                Console.WriteLine($"U? = new A()  (Nullable<U>)            : {(t11.HasValue ? Show(t11.Value) : "no value")}");
    U? t12 = null;                                   Console.WriteLine($"U? = null                              : {(t12.HasValue ? Show(t12.Value) : "no value")}");
    U t13 = (U)new A();                              Console.WriteLine($"(U)new A() explicit cast of a case    : {Show(t13)}");
    object o = new A();
#if EXPLICIT_FROM_OBJECT
    U t14 = (U)o;
#endif
    Console.WriteLine($"is/as: o is U = {o is U}");

#if INFER_TASK
    Task<U> x1 = Task.FromResult(new A());
#endif
#if VAR_TERNARY
    var x2 = flag ? new A() : new B();
#endif
#if TASK_CASE_TO_TASK_UNION
    Task<A> ta = Task.FromResult(new A()); Task<U> x3 = ta;
#endif
#if ENUMERABLE_COVARIANCE
    IEnumerable<A> ea = [new A()]; IEnumerable<U> x4 = ea;
#endif
#if INFER_TWO_ARGS
    Take(new A(), new B());
#endif
}

static string Show(System.Runtime.CompilerServices.IUnion u) => u.Value is null ? "null" : u.Value is long or double or string or int ? $"{u.Value.GetType().Name} {u.Value}" : u.Value.GetType().Name;
static T Id<T>(T t) => t;
static void Take<T>(T a, T b) { }
static async Task<U> AsyncReturn(bool ok) { await Task.Yield(); if (ok) return new A(); return new B(); }
static async ValueTask<U> AsyncValueReturn() { await Task.Yield(); return new B(); }

public class Base;
public class Derived : Base;
public interface IFoo;
public interface IBar;
public class Both : IFoo, IBar;
public record A;
public record B;

public union L(IEnumerable<int>, IList<int>);
public union BD(Base, Derived);
public union DB(Derived, Base);
public union LD(long, double);
public union OS(object, string);
public union Sib(IFoo, IBar);
public union OneOrMore<T>(T, IEnumerable<T>);
public union U(A, B);
