using System;

internal static class FunctionalExamples
{
    internal static void Verify()
    {
        Result<int> success = new Success<int>(21);
        var rejection = new Failure("Rejected");
        Result<int> failure = rejection;

        Require(success.Map(static value => value * 2).Match(static value => value == 42, static _ => false),
            "Map transforms success");
        Require(success.Map(static value => value.ToString()).Match(static value => value == "21", static _ => false),
            "Map changes payload type");
        Require(failure.Map<int, string>(static _ => throw new Exception("Map must skip failure"))
            .Match(static _ => false, error => ReferenceEquals(error, rejection)), "Map preserves failure");

        Require(success.Bind(AddOne).Match(static value => value == 22, static _ => false), "Bind success");
        Require(success.Bind<int, string>(_ => rejection)
            .Match(static _ => false, error => ReferenceEquals(error, rejection)), "Bind returns callback failure");
        Require(failure.Bind<int, string>(static _ => throw new Exception("Bind must skip failure"))
            .Match(static _ => false, error => ReferenceEquals(error, rejection)), "Bind preserves failure");
        Require(success.Match(static value => value, static _ => throw new Exception("Wrong Match branch")) == 21,
            "Match selects success");
        Require(failure.Match(static _ => throw new Exception("Wrong Match branch"), static error => error.Reason)
            == "Rejected", "Match selects failure");
        var calls = 0;
        _ = success.Map(value => { calls++; return value; });
        Require(calls == 1, "Map invokes its success callback once");
        calls = 0;
        _ = success.Bind(value => { calls++; return Pure(value); });
        Require(calls == 1, "Bind invokes its success callback once");
        calls = 0;
        _ = success.Match(value => { calls++; return value; }, static _ => 0);
        Require(calls == 1, "Match invokes its selected callback once");
        Console.WriteLine("functional-map-bind-match-and-short-circuit-verified");

        foreach (var result in new Result<int>[] { success, failure })
        {
            Require(Describe(result.Map(static value => value)) == Describe(result), "Map identity");
            Require(Describe(result.Map(static value => value + 1).Map(static value => value * 2))
                == Describe(result.Map(static value => (value + 1) * 2)), "Map composition");
            Require(Describe(result.Bind(Pure)) == Describe(result), "Bind right identity");
            Require(Describe(result.Bind(AddOne).Bind(Double))
                == Describe(result.Bind(static value => AddOne(value).Bind(Double))), "Bind associativity");
        }
        Require(Describe(Pure(21).Bind(AddOne)) == Describe(AddOne(21)), "Bind left identity");
        Require(FunctionalPipeline.Describe("21") == "Doubled: 42", "Successful pure pipeline");
        Require(FunctionalPipeline.Describe("invalid") == "Rejected: Expected an integer.", "Parse failure");
        Require(FunctionalPipeline.Describe("0") == "Rejected: Expected a positive integer.", "Validation failure");
        Require(FunctionalPipeline.Describe("-1") == "Rejected: Expected a positive integer.", "Negative value");
        Require(FunctionalPipeline.Describe("2147483647") == "Doubled: 4294967294", "Widen before multiplying");
        Require(FunctionalPipeline.Describe("2147483648") == "Rejected: Expected an integer.", "Parse overflow");
        Console.WriteLine("functional-laws-and-pure-pipeline-verified");

        Result<int> empty = default;
        RequireUninitialized(() => empty.Map(static value => value));
        RequireUninitialized(() => empty.Bind(Pure));
        RequireUninitialized(() => empty.Match(static value => value, static _ => 0));
        RequireNullArgument(() => success.Map<int, int>(null!), "map");
        RequireNullArgument(() => success.Bind<int, int>(null!), "bind");
        RequireNullArgument(() => success.Match<int, int>(null!, static _ => 0), "success");
        RequireNullArgument(() => failure.Match<int, int>(static value => value, null!), "failure");

        var callbackError = new InvalidOperationException("Callback error");
        RequireSameException(() => success.Map<int, int>(_ => throw callbackError), callbackError);
        RequireSameException(() => success.Bind<int, int>(_ => throw callbackError), callbackError);
        RequireSameException(() => success.Match<int, int>(_ => throw callbackError, static _ => 0), callbackError);
        RequireSameException(() => failure.Match<int, int>(static value => value, _ => throw callbackError), callbackError);
        Console.WriteLine("functional-default-arguments-and-exceptions-verified");
    }

    private static Result<int> Pure(int value) => new Success<int>(value);
    private static Result<int> AddOne(int value) => new Success<int>(value + 1);
    private static Result<int> Double(int value) => new Success<int>(value * 2);
    private static string Describe(Result<int> result) => result.Match(
        static value => $"success:{value}", static failure => $"failure:{failure.Reason}");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RequireUninitialized(Action action)
    {
        try { action(); }
        catch (InvalidOperationException error) when (error.Message == "Result was not initialized.") { return; }
        throw new InvalidOperationException("Expected rejection of an uninitialized Result.");
    }

    private static void RequireNullArgument(Action action, string parameter)
    {
        try { action(); }
        catch (ArgumentNullException error) when (error.ParamName == parameter) { return; }
        throw new InvalidOperationException("Expected ArgumentNullException: " + parameter);
    }

    private static void RequireSameException(Action action, Exception expected)
    {
        try { action(); }
        catch (Exception error) when (ReferenceEquals(error, expected)) { return; }
        throw new InvalidOperationException("Callback exceptions must propagate unchanged.");
    }
}
