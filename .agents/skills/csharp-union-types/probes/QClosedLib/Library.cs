namespace Lib;

public closed record Transportation;
public sealed record Plane(string FlightNumber) : Transportation;
public sealed record Train(string Operator) : Transportation;
#if THIRD
public sealed record Helicopter(string Operator) : Transportation;
#endif

public closed record Layer;
public record Mid : Layer;                  // not sealed: the extension point

public closed record Box<T>;
public sealed record BoxA<T>(T Value) : Box<T>;
public sealed record BoxB<T> : Box<T>;
