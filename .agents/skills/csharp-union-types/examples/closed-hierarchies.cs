using System;
using System.Globalization;

public closed record GateState;
public sealed record GateClosed : GateState;
public sealed record GateOpen : GateState;
public closed record GateMoving(float Percent) : GateState;
public sealed record GateOpening(float Percent) : GateMoving(Percent);
public sealed record GateClosing(float Percent) : GateMoving(Percent);

public static class ClosedHierarchyExamples
{
    // GateMoving is closed too: the compiler can check coverage down to its leaves.
    // An ordinary nonsealed GateMoving would remain an open branch instead.
    public static string Describe(GateState state) => state switch
    {
        GateClosed => "closed",
        GateOpen => "open",
        GateOpening(var percent) => "opening " + percent.ToString("0.##", CultureInfo.InvariantCulture) + "%",
#if !MISSING_CLOSED_LEAF
        GateClosing(var percent) => "closing " + percent.ToString("0.##", CultureInfo.InvariantCulture) + "%",
#endif
        null => throw new ArgumentNullException(nameof(state)),
    };

    public static void Verify()
    {
        GateState[] states = [new GateClosed(), new GateOpen(), new GateOpening(25), new GateClosing(75)];
        string[] expected = ["closed", "open", "opening 25%", "closing 75%"];
        for (var index = 0; index < states.Length; index++)
        {
            if (Describe(states[index]) != expected[index])
                throw new InvalidOperationException("Closed hierarchy leaf was matched incorrectly.");
        }
        try
        {
            Describe(default!);
            throw new InvalidOperationException("Expected null input rejection.");
        }
        catch (ArgumentNullException error) when (error.ParamName == "state") { }
        Console.WriteLine("closed-hierarchy-leaves-and-null-verified");
    }
}
