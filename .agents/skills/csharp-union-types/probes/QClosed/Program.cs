#nullable enable
using Lib;

// Cross-assembly behaviour of `closed`. QClosedLib is the library; build it with
// -p:DefineConstants=THIRD to add a third case (Helicopter) without rebuilding this project,
// then copy its dll over this project's output.

foreach (Transportation x in new Transportation[] { new Plane("LH1"), new Train("DB") })
    Console.WriteLine($"{x} => {ToLabel(x)}");

var heli = typeof(Transportation).Assembly.GetType("Lib.Helicopter");
Console.WriteLine($"Lib has Helicopter: {heli is not null}");
if (heli is not null)
{
    try { Console.WriteLine(ToLabel((Transportation)Activator.CreateInstance(heli, "HeliAir")!)); }
    catch (Exception e) { Console.WriteLine($"Helicopter => {e.GetType().Name}"); }
}

Console.WriteLine($"Box => {Box(new BoxA<int>(3))}, {Box(new BoxB<int>())}");
Console.WriteLine($"MyMid via Mid arm => {LayerLabel(new MyMid())}");

static string ToLabel(Transportation t) => t switch
{
    Plane p => $"flight {p.FlightNumber}",
    Train r => r.Operator,
};

static string Box(Box<int> b) => b switch { BoxA<int> a => $"A{a.Value}", BoxB<int> => "B" };

// Closure is not transitive: Mid is not sealed, so another assembly may derive from it.
static string LayerLabel(Layer l) => l switch { Mid => "mid" };

public sealed record MyMid : Mid;

#if DERIVE_CLOSED
public sealed record Foreign : Transportation;      // expect CS9382
#endif
#if ABSTRACT_CLOSED
public abstract closed record Bad;                  // expect CS9384
#endif
#if CLOSED_INTERFACE
public closed interface IBad;                       // expect CS0106
#endif
#if PARTIAL_ORDER
public partial closed record P1;                    // expect CS0267
#endif
#if PARTIAL_OK
public closed partial record P2;
#endif
#if NONEXHAUSTIVE
public static class N1 { public static string Short(Transportation t) => t switch { Plane => "p" }; }   // expect CS8509
#endif
#if NULLABLE_INPUT
public static class N2 { public static string F(Transportation? t) => t switch { Plane => "p", Train => "t" }; }   // expect CS8655
#endif
#if WHEN_ARM
public static class N3 { public static string F(Transportation t) => t switch { Plane p when p.FlightNumber == "X" => "p", Train => "t" }; }   // expect CS8846
#endif
