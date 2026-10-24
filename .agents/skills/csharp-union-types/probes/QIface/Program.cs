#nullable enable
// Type patterns against an interface, on a union. RC 1: the union instance is tested first, so
// `u is IMarker` is always true when the union declares IMarker. RC 2: Value only.
// Build with -p:DefineConstants=NAME for the cases that are expected not to compile.

Console.WriteLine("--- union declares IMarker, no case implements it ---");
Console.WriteLine($"open T: {OpenT<int>(5)} (int), {OpenT<Marked>(new Marked())} (Marked : IMarker)");
static bool OpenT<T>(T v) { GenUnion<T> g = v; return g is IMarker; }

#if CLOSED_INT
GenUnion<int> gi = 5;
Console.WriteLine($"gi is IMarker: {gi is IMarker}");
#endif

Console.WriteLine("--- a case implements IPet ---");
Pet cat = new Cat("Tom");
Pet bird = new Bird("Tweety");
Pet none = default;
Console.WriteLine($"cat is IPet: {cat is IPet}; bird is IPet: {bird is IPet}; default is IPet: {none is IPet}");
Console.WriteLine($"cat is IPet ip => {(cat is IPet ip ? ip.Name : "no")}");

#if NO_CASE_IMPLEMENTS
// Neither case type implements IPet and the union does not declare it: the spec says error.
Plant plant = new Fern();
Console.WriteLine($"plant is IPet: {plant is IPet}");
#endif

#if NO_CASE_IMPLEMENTS_SEALED
// Same, with sealed cases, so no subtype could implement it either.
SealedU su = new SealedA();
Console.WriteLine($"su is IPet: {su is IPet}");
#endif

public interface IMarker;
public interface IPet { string Name { get; } }
public record Cat(string Name) : IPet;
public record Bird(string Name) : IPet;
public union Pet(Cat, Bird);

public record Fern;
public record Moss;
public union Plant(Fern, Moss);

public sealed record SealedA;
public sealed record SealedB;
public union SealedU(SealedA, SealedB);

public record Marked : IMarker;
public union GenUnion<T>(T, string) : IMarker;
