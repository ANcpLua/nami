using System.Xml.Linq;
using JetBrains.Annotations;

namespace Nami.Tests;

/// <summary>A project target described as data, without executing an MSBuild task.</summary>
[PublicAPI]
public sealed class FixtureTarget
{
    private readonly List<XElement> _tasks = [];

    public FixtureTarget(string name, string? beforeTargets = null, string? afterTargets = null,
        string? condition = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        BeforeTargets = beforeTargets;
        AfterTargets = afterTargets;
        Condition = condition;
    }

    public string Name { get; }
    public string? BeforeTargets { get; }
    public string? AfterTargets { get; }
    public string? Condition { get; }

    public FixtureTarget Message(string text, string? condition = null)
    {
        return AddTask("Message", text, null, condition);
    }

    public FixtureTarget Warning(string code, string text, string? condition = null)
    {
        return AddTask("Warning", text, code, condition);
    }

    public FixtureTarget Error(string code, string text, string? condition = null)
    {
        return AddTask("Error", text, code, condition);
    }

    private FixtureTarget AddTask(string name, string text, string? code, string? condition)
    {
        ArgumentNullException.ThrowIfNull(text);
        var task = new XElement(name, new XAttribute("Text", text));
        if (name == "Message") task.Add(new XAttribute("Importance", "high"));
        if (code is not null) task.Add(new XAttribute("Code", code));
        if (condition is not null) task.Add(new XAttribute("Condition", condition));
        _tasks.Add(task);
        return this;
    }

    internal XElement ToElement()
    {
        var target = new XElement("Target", new XAttribute("Name", Name));
        if (BeforeTargets is not null) target.Add(new XAttribute("BeforeTargets", BeforeTargets));
        if (AfterTargets is not null) target.Add(new XAttribute("AfterTargets", AfterTargets));
        if (Condition is not null) target.Add(new XAttribute("Condition", Condition));
        target.Add(_tasks.Select(task => new XElement(task)));
        return target;
    }
}
