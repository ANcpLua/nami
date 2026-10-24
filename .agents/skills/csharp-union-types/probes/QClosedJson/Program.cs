#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

// The data-less case from dotnet/runtime#135742 modelled as a closed hierarchy instead of a union.
// NotRequired and PendingApproval both carry no data; the type discriminator is what tells them apart.

Approval[] all =
[
    new NotRequired(), new PendingApproval(),
    new PartlyApproved(Guid.Empty), new FullyApproved(Guid.Empty, Guid.Empty), new Rejected(Guid.Empty),
];

foreach (var a in all)
{
    var json = JsonSerializer.Serialize(a);
    var back = JsonSerializer.Deserialize<Approval>(json);
    Console.WriteLine($"{a.GetType().Name,-16} {json}");
    Console.WriteLine($"{"",-16} round-trip type: {back?.GetType().Name}  same: {back?.GetType() == a.GetType()}");
}

Console.WriteLine($"Label: {Label(new PendingApproval())}");

try { Console.WriteLine(JsonSerializer.Deserialize<Approval>("{}")); }
catch (Exception e) { Console.WriteLine($"no discriminator => {e.GetType().Name}: {e.Message}"); }

try { Console.WriteLine(JsonSerializer.Deserialize<Approval>("""{"$type":"Bogus"}""")); }
catch (Exception e) { Console.WriteLine($"unknown discriminator => {e.GetType().Name}: {e.Message}"); }

static string Label(Approval a) => a switch
{
    NotRequired => "not required",
    PendingApproval => "pending",
    PartlyApproved => "partly",
    FullyApproved => "fully",
    Rejected => "rejected",
};

[JsonPolymorphic(InferClosedTypePolymorphism = true)]
public closed record class Approval;
public sealed record NotRequired : Approval;
public sealed record PendingApproval : Approval;
public sealed record PartlyApproved(Guid Approver) : Approval;
public sealed record FullyApproved(Guid Approver1, Guid Approver2) : Approval;
public sealed record Rejected(Guid Rejector) : Approval;
