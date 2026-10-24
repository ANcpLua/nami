using System;
using System.Runtime.CompilerServices;

// The union keyword generates the storage, case constructors, [Union] and IUnion.
// Benefit: callers can return either case directly and switch over the known cases.
// Suggestion: keep this concise declaration for these reference-type cases.
public sealed record Accepted(string Message);
public sealed record Rejected(string Reason);
public readonly union Outcome(Accepted, Rejected);

public static class OutcomeText
{
    // Matching the union directly preserves compiler exhaustiveness checking.
    // Optional style: Accepted(var message) => message (and likewise for Rejected).
    public static string Describe(Outcome value) => value switch
    {
        Accepted accepted => accepted.Message,
        Rejected rejected => rejected.Reason,
        // A default union has no contained value. Handle it explicitly instead of
        // using a catch-all that would also hide newly added cases from the compiler.
        null => throw new InvalidOperationException("Outcome was not initialized."),
    };
}

// Example 2: distinct case types preserve the unit alongside its numeric value.
// Benefit: conversion stays with the type, and callers can supply either unit.
// Suggestion: keep the concise union and computed property. These record classes
// allocate case objects; making them record structs would instead box them in
// the generated union's object storage, so that change alone does not avoid allocation.
public sealed record Meters(double Value);
public sealed record Feet(double Value);
public readonly union Length(Meters, Feet)
{
    public static Length Create(double value, bool isMetric) =>
        isMetric ? new Meters(value) : new Feet(value);

    public double TotalMeters => this switch
    {
        Meters meters => meters.Value,
        Feet feet => feet.Value * 0.3048,
        // An uninitialized length is invalid, rather than implicitly zero meters.
        null => throw new InvalidOperationException("Length was not initialized."),
    };
}

// Example 3: [Union] opts this custom struct into C# union behavior.
// Benefit: typed fields avoid boxing on construction; HasValue/TryGetValue let
// the compiler match null/int/bool without going through the boxed Value property.
// Tradeoff: this representation requires more code and consistent access members.
// Suggestion: keep it when avoiding scalar allocations matters. Otherwise replace
// this entire struct with the simpler declaration (non-null scalars will be boxed):
// public readonly union NumberOrFlag(int?, bool?);
// The nullable constructors deliberately map null to the empty state, while
// preserving 0 and false as valid cases. default(NumberOrFlag) is also empty.
[Union]
public readonly struct NumberOrFlag : IUnion
{
    private readonly byte _tag;
    private readonly int _number;
    private readonly bool _flag;

    public NumberOrFlag(int? value)
    {
        _tag = value.HasValue ? (byte)1 : (byte)0;
        _number = value.GetValueOrDefault();
        _flag = false;
    }

    public NumberOrFlag(bool? value)
    {
        _tag = value.HasValue ? (byte)2 : (byte)0;
        _number = 0;
        _flag = value.GetValueOrDefault();
    }

    // Required union access: a non-null scalar is boxed whenever Value is read.
    public object? Value => _tag switch
    {
        1 => _number,
        2 => _flag,
        _ => null,
    };

    // These members must agree with Value: presence means a non-null contained
    // value, and a successful TryGetValue returns the matching scalar case.
    public bool HasValue => _tag is not 0;
    public bool TryGetValue(out int value) { value = _number; return _tag is 1; }
    public bool TryGetValue(out bool value) { value = _flag; return _tag is 2; }
}

// Example 4: a generic result keeps the successful value strongly typed.
// Distinct case types avoid overlapping success/error types, even for T=string.
// This is still object-backed union storage; generics do not remove allocations.
public sealed record Success<T>(T Value);
public sealed record Failure(string Reason);
public readonly union Result<T>(Success<T>, Failure);
