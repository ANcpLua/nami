namespace UnionDiagnostics;

public sealed class Cat { }
public sealed class Dog { }

#if NO_CASES
public union Empty;
#endif
#if CLOSED_NAME
public class closed { }
#endif
#if SEALED_CLOSED
public sealed closed class CannotBeSealed { }
#endif
#if UNBOUND_CLOSED_PARAMETER
public closed class ClosedBase { }
public sealed class Unbound<T> : ClosedBase { }
#endif
#if ABSTRACT_CLOSED
public abstract closed class AlreadyAbstract { }
#endif
#if NONBOXABLE_CASE
public union NonBoxable(System.Span<int>);
#endif
#if INSTANCE_FIELD
public union HasField(Cat, Dog) { private int _field; }
#endif
#if PUBLIC_CONSTRUCTOR
public union ExtraConstructor(Cat, Dog)
{
    public ExtraConstructor(int value) : this(new Cat()) { }
}
#endif
#if MISSING_INITIALIZER
public union MissingInitializer(Cat, Dog)
{
    public MissingInitializer(int value, bool marker) { }
}
#endif
#if MISSING_CREATION
[System.Runtime.CompilerServices.Union]
public struct MissingCreation { public object? Value => null; }
#endif
#if MISSING_PROVIDER_VALUE
[System.Runtime.CompilerServices.Union]
public struct MissingValue : MissingValue.IUnionMembers
{
    public interface IUnionMembers
    {
        public static MissingValue Create(Cat value) => default;
    }
}
#endif
#if FORBIDDEN_PROVIDER
public union HasProvider(Cat, Dog) : HasProvider.IUnionMembers
{
    public interface IUnionMembers { object? Value { get; } }
}
#endif
#if UNREACHABLE_ARM
public union Pet(Cat, Dog);
public static class Match
{
    public static int Read(Pet pet) => pet switch { Cat => 1, Cat => 2, Dog => 3, null => 0 };
}
#endif
