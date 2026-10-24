using System;
using System.Globalization;

// Transform values with Map, compose fallible functions with Bind, and consume
// both cases with Match. These operators are pure when the supplied functions
// are pure. They do not catch callback exceptions or perform I/O.
public static class ResultExtensions
{
    public static Result<U> Map<T, U>(this Result<T> source, Func<T, U> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return source switch
        {
            Success<T>(var value) => new Success<U>(map(value)),
            Failure failure => failure,
            null => throw new InvalidOperationException("Result was not initialized."),
        };
    }

    public static Result<U> Bind<T, U>(this Result<T> source, Func<T, Result<U>> bind)
    {
        ArgumentNullException.ThrowIfNull(bind);
        return source switch
        {
            Success<T>(var value) => bind(value),
            Failure failure => failure,
            null => throw new InvalidOperationException("Result was not initialized."),
        };
    }

    public static U Match<T, U>(this Result<T> source, Func<T, U> success, Func<Failure, U> failure)
    {
        ArgumentNullException.ThrowIfNull(success);
        ArgumentNullException.ThrowIfNull(failure);
        return source switch
        {
            Success<T>(var value) => success(value),
            Failure rejected => failure(rejected),
            null => throw new InvalidOperationException("Result was not initialized."),
        };
    }
}

public static class FunctionalPipeline
{
    // Expected input errors stay in Result. Explicit culture keeps parsing and
    // rendering independent of ambient state. I/O belongs at the caller boundary.
    public static Result<int> ParseInteger(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? new Success<int>(value)
            : new Failure("Expected an integer.");

    public static Result<int> RequirePositive(int value) => value > 0
        ? new Success<int>(value)
        : new Failure("Expected a positive integer.");

    public static string Describe(string text) => ParseInteger(text)
        .Bind(RequirePositive)
        .Map(static value => (long)value * 2)
        .Match(
            static value => string.Create(CultureInfo.InvariantCulture, $"Doubled: {value}"),
            static failure => $"Rejected: {failure.Reason}");
}
