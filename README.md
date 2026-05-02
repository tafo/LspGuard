# LspGuard

LspGuard is a Roslyn analyzer for C#. It catches Liskov Substitution Principle violations at compile time.

## Why I built this

I was talking with friends about Shift Left. One question kept coming up. How do static analysis tools actually work?

A while ago I wrote an article about the Liskov Substitution Principle. I wanted to combine the two ideas. So I started this project. The goal is to learn how Roslyn analyzers are built and to produce something useful at the end.

## What it does

LspGuard reads your C# code during compilation. It looks at override methods and compares them with their base methods. If the override breaks the base contract, LspGuard reports a warning. The warning shows up in the IDE as a yellow squiggle. It also appears in the build output.

## Rules

I plan to ship seven rules. One per day.

| ID     | What it catches                                              | Status     |
|--------|--------------------------------------------------------------|------------|
| LSP001 | Override adds a null check the base does not have            | Done       |
| LSP002 | Override adds a range or value check the base does not have  | Planned    |
| LSP003 | Override throws a new unchecked exception type               | Planned    |
| LSP004 | Override throws NotImplementedException or NotSupportedException | Planned |
| LSP005 | Override narrows a parameter type via a generic constraint   | Planned    |
| LSP006 | Override widens the return type contract                     | Planned    |
| LSP007 | Override property setter adds validation absent in the base  | Planned    |

## Example

Here is what LSP001 catches:

```csharp
public class Logger
{
    public virtual void Log(string message) { }
}

public class StrictLogger : Logger
{
    public override void Log(string message)
    {
        ArgumentNullException.ThrowIfNull(message); // LSP001
    }
}
```

The base method accepts null. The override rejects null. A caller that holds a `Logger` reference does not expect this. LspGuard flags it.

## Install

Once it is on NuGet you will be able to add it to any project:

```
dotnet add package LspGuard
```

## What it does not do

LspGuard only catches violations that can be decided by static analysis. It does not prove postcondition correctness. It does not verify invariants at runtime. It does not promise to catch every LSP issue. The aim is to catch the common ones with high confidence and zero false positives.

## License

MIT
