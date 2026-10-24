using System;
using System.Runtime.CompilerServices;

public sealed class Cat(string name) { public string Name { get; } = name; }
public sealed class Dog { }
public union Pet(Cat, Dog);

internal static class Consumer
{
#if !NETSTANDARD2_0
    public static void Main() => Verify();
#endif

    private static string Name(Pet value) => value switch
    {
        Cat cat => cat.Name,
        Dog => "dog",
        null => "empty"
    };

    public static void Verify()
    {
        Pet pet = new Cat("Milo");
        IUnion union = pet;
        if (union.Value is not Cat { Name: "Milo" } || Name(pet) != "Milo")
            throw new InvalidOperationException("Case conversion or matching failed.");
        if (default(Pet) is not null || Name(default) != "empty")
            throw new InvalidOperationException("Default contents must be null.");
        var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(UnionAttribute), typeof(AttributeUsageAttribute))!;
        if (usage.ValidOn != (AttributeTargets.Class | AttributeTargets.Struct) || usage.AllowMultiple || usage.Inherited)
            throw new InvalidOperationException("Incorrect UnionAttribute metadata.");
#if NET11_0_OR_GREATER
        if (typeof(IUnion).Assembly != typeof(object).Assembly || typeof(UnionAttribute).Assembly != typeof(object).Assembly)
            throw new InvalidOperationException(".NET 11 must use the framework support types.");
        Console.WriteLine("framework-union-types-verified");
#else
        if (typeof(IUnion).Assembly != typeof(Consumer).Assembly || typeof(UnionAttribute).Assembly != typeof(Consumer).Assembly)
            throw new InvalidOperationException("Legacy targets must use the local support types.");
        Console.WriteLine("polyfilled-union-types-verified");
#endif
        Console.WriteLine("case-default-and-attribute-verified");
    }
}
