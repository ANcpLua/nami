using System;

#if FUNCTIONAL_EXAMPLES
FunctionalExamples.Verify();
#endif

Outcome accepted = new Accepted("Stored");
Outcome rejected = new Rejected("Invalid project");
Require(OutcomeText.Describe(accepted) == "Stored", "Accepted text");
Require(OutcomeText.Describe(rejected) == "Invalid project", "Rejected text");
RequireThrows(() => OutcomeText.Describe(default), "Outcome was not initialized.");
Console.WriteLine("outcome-cases-and-default-verified");

var metric = Length.Create(10, isMetric: true);
var imperial = Length.Create(10, isMetric: false);
Require(metric is Meters && metric.TotalMeters == 10, "Metric unit");
Require(imperial is Feet && Math.Abs(imperial.TotalMeters - 3.048) < 1e-12, "Imperial conversion");
RequireThrows(() => { Length empty = default; _ = empty.TotalMeters; }, "Length was not initialized.");
Console.WriteLine("length-units-and-default-verified");

foreach (var expected in new[] { 0, int.MinValue, int.MaxValue })
{
    NumberOrFlag value = expected;
    Require(value.HasValue && value.TryGetValue(out int number) && number == expected, "Integer access");
    Require(!value.TryGetValue(out bool flag) && !flag, "Integer is not boolean");
    Require(value.Value is int boxed && boxed == expected, "Integer Value consistency");
    Require(value switch { int n => n == expected, bool => false, null => false }, "Integer matching");
}

foreach (var expected in new[] { false, true })
{
    NumberOrFlag value = expected;
    Require(value.HasValue && value.TryGetValue(out bool flag) && flag == expected, "Boolean access");
    Require(!value.TryGetValue(out int number) && number == 0, "Boolean is not integer");
    Require(value.Value is bool boxed && boxed == expected, "Boolean Value consistency");
    Require(value switch { bool b => b == expected, int => false, null => false }, "Boolean matching");
}

foreach (var value in new NumberOrFlag[] { default, new((int?)null), new((bool?)null) })
{
    Require(!value.HasValue && value.Value is null && value is null, "Empty state");
    Require(!value.TryGetValue(out int number) && number == 0, "Empty integer access");
    Require(!value.TryGetValue(out bool flag) && !flag, "Empty boolean access");
}
Console.WriteLine("scalar-values-and-empty-states-verified");

Result<int> integerResult = new Success<int>(0);
Result<string> textResult = new Success<string>("Stored");
Result<int> failureResult = new Failure("Rejected");
Require(ReadResult(integerResult) == 0, "Generic integer result");
Require(ReadResult(textResult) == "Stored", "Generic string result");
RequireThrows(() => ReadResult(failureResult), "Rejected");
RequireThrows(() => ReadResult(default(Result<int>)), "Result was not initialized.");
Console.WriteLine("generic-result-success-failure-and-default-verified");

#if WRONG_GENERIC_CASE
Result<int> wrongCase = new Success<string>("Wrong type");
#endif

#if MISSING_GENERIC_CASE
_ = IncompleteResult(integerResult);
static T IncompleteResult<T>(Result<T> value) => value switch
{
    Success<T> success => success.Value,
    null => throw new InvalidOperationException("Result was not initialized."),
};
#endif

// Warm the exact path, then measure construction and typed matching together.
_ = MatchScalars(100);
var before = GC.GetAllocatedBytesForCurrentThread();
var sum = MatchScalars(100);
var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
Require(sum == 5050, "Scalar matching result");
Require(allocated == 0, "Scalar construction and matching allocated " + allocated + " bytes");
Console.WriteLine("scalar-construction-and-matching-allocated-bytes: 0");

static T ReadResult<T>(Result<T> value) => value switch
{
    Success<T> success => success.Value,
    Failure failure => throw new InvalidOperationException(failure.Reason),
    null => throw new InvalidOperationException("Result was not initialized."),
};

static int MatchScalars(int count)
{
    var sum = 0;
    for (var i = 0; i < count; i++)
    {
        NumberOrFlag number = (int?)i;
        NumberOrFlag flag = (bool?)true;
        sum += number switch { int n => n, bool => -1, null => -1 };
        sum += flag switch { bool b => b ? 1 : 0, int => -1, null => -1 };
        NumberOrFlag empty = default;
        if (empty is not null) sum--;
    }

    return sum;
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void RequireThrows(Action action, string message)
{
    try { action(); }
    catch (InvalidOperationException exception) when (exception.Message == message) { return; }
    throw new InvalidOperationException("Expected InvalidOperationException: " + message);
}
