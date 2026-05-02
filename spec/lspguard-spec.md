# LspGuard — Specification

A Roslyn analyzer for detecting Liskov Substitution Principle violations in C# code.

## What this document is

A specification for Claude Code to scaffold the project and implement rules incrementally. Architectural decisions are already made. Each rule is implemented one at a time, on separate days.

---

## Goals

- Detect LSP violations that are decidable through static analysis
- Provide diagnostics through standard Roslyn `DiagnosticAnalyzer` infrastructure
- Distribute as a NuGet package
- Stay extensible — adding a new rule means adding a new analyzer class, nothing else

## Non-goals

- Detecting runtime-decidable violations (full postcondition weakening proofs, invariant preservation)
- Behavioral equivalence verification
- Auto-fixes in the first version (code fixes come later, after rules stabilize)

---

## Target

- **Framework:** .NET 10
- **Language:** C# 13
- **Roslyn:** `Microsoft.CodeAnalysis.CSharp` (latest stable compatible with .NET 10 SDK)
- **Distribution:** NuGet package `LspGuard`
- **Consumer requirement:** Any project with a Roslyn version compatible with the analyzer's target. Analyzer assemblies target `netstandard2.0` so they load in a wide range of consumer projects, even though the solution uses .NET 10 for tests and tooling.

> Note on targeting: Roslyn analyzer assemblies are loaded by the compiler, not by the consumer's runtime. They must target `netstandard2.0`. The analyzer DLL itself is `netstandard2.0`. The test project and any sample projects target `net10.0`.

---

## Solution structure

```
LspGuard/
├── LspGuard.sln
├── src/
│   ├── LspGuard/
│   │   ├── LspGuard.csproj                  # netstandard2.0, the analyzer
│   │   ├── DiagnosticIds.cs                 # LSP001..LSP007 constants
│   │   ├── DiagnosticDescriptors.cs         # All DiagnosticDescriptor instances
│   │   ├── Internal/
│   │   │   ├── OverrideAnalysisHelpers.cs   # Shared: find base method, etc.
│   │   │   ├── ExceptionAnalysisHelpers.cs  # Shared: throw statement scanning
│   │   │   └── ContractAnalysisHelpers.cs   # Shared: precondition pattern detection
│   │   └── Analyzers/
│   │       ├── PreconditionStrengtheningNullCheckAnalyzer.cs   # LSP001
│   │       ├── PreconditionStrengtheningRangeCheckAnalyzer.cs  # LSP002
│   │       ├── NewUncheckedExceptionAnalyzer.cs                # LSP003
│   │       ├── NotImplementedOverrideAnalyzer.cs               # LSP004
│   │       ├── ParameterTypeNarrowingAnalyzer.cs               # LSP005
│   │       ├── ReturnTypeWideningAnalyzer.cs                   # LSP006
│   │       └── SetterValidationStrengtheningAnalyzer.cs        # LSP007
│   └── LspGuard.Package/
│       └── LspGuard.Package.csproj          # Wraps analyzer for NuGet packing
├── tests/
│   └── LspGuard.Tests/
│       ├── LspGuard.Tests.csproj            # net10.0
│       ├── Verifiers/                       # Roslyn analyzer testing helpers
│       └── Analyzers/
│           ├── PreconditionStrengtheningNullCheckTests.cs
│           ├── ...                          # one test file per analyzer
└── README.md
```

---

## Architectural decisions

### One analyzer per rule (Roslyn-native)

Each rule is its own `DiagnosticAnalyzer` class. This is the standard Roslyn pattern. IDEs, the compiler, suppression mechanisms, and `.editorconfig` all work natively with this layout.

### Shared logic in static helpers, not base classes

Analyzers do not inherit from a common abstract base. Roslyn discourages this — analyzer registration happens in `Initialize`, and inheritance hides registration intent. Instead, common logic lives in static helper classes under `Internal/`:

- `OverrideAnalysisHelpers` — given an `IMethodSymbol`, return the overridden base symbol; check if a method is an override at all
- `ExceptionAnalysisHelpers` — scan a method body for `throw` statements and return exception types
- `ContractAnalysisHelpers` — detect common precondition patterns (`ArgumentNullException.ThrowIfNull`, `if (x == null) throw`, range checks)

Each analyzer registers its own actions and calls helpers as needed.

### Diagnostic ID range

`LSP001` through `LSP099` reserved for this project. First seven assigned below. New rules pick the next free number.

### Severity and category

- All diagnostics: category `"Design"`, default severity `Warning`, enabled by default
- Each diagnostic has a `helpLinkUri` pointing to a documentation page (placeholder URL for now, e.g. `https://github.com/<your-username>/LspGuard/blob/main/docs/LSPxxx.md`)

---

## Diagnostic IDs

| ID     | Title                                    | Category | Severity |
|--------|------------------------------------------|----------|----------|
| LSP001 | Override adds null check absent in base  | Design   | Warning  |
| LSP002 | Override adds range/value check absent in base | Design | Warning |
| LSP003 | Override throws new unchecked exception type | Design | Warning |
| LSP004 | Override throws NotImplementedException or NotSupportedException | Design | Warning |
| LSP005 | Override narrows parameter type via generic constraint | Design | Warning |
| LSP006 | Override widens return type contract     | Design   | Warning  |
| LSP007 | Override property setter adds validation absent in base | Design | Warning |

---

## Rules in detail

### LSP001 — Precondition strengthening via null check

**Detects:** An override method that adds an `ArgumentNullException.ThrowIfNull(x)`, `if (x is null) throw`, `if (x == null) throw`, or equivalent null check on a parameter, where the base method body has no such check.

**Why it's an LSP violation:** Caller code written against the base contract may pass null, expecting it to be handled (or to fail in a different way). The override now rejects it — a stricter precondition.

**Violating example:**

```csharp
public class Logger
{
    public virtual void Log(string message) { /* writes message, null becomes "" */ }
}

public class StrictLogger : Logger
{
    public override void Log(string message)
    {
        ArgumentNullException.ThrowIfNull(message); // LSP001
        // ...
    }
}
```

**Compliant example:** The override either also accepts null (does not add the check) or the base method already documents and enforces the same precondition.

---

### LSP002 — Precondition strengthening via range/value check

**Detects:** An override method that adds a guard clause comparing a parameter against a constant (`if (x < 0) throw`, `if (x > MAX) throw`, `if (string.IsNullOrEmpty(s)) throw`) where the base method body has no such guard.

**Why it's an LSP violation:** Same shape as LSP001 — narrows the accepted input domain.

**Violating example:**

```csharp
public class Counter
{
    public virtual void Add(int value) { _total += value; }
}

public class PositiveCounter : Counter
{
    public override void Add(int value)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(); // LSP002
        _total += value;
    }
}
```

---

### LSP003 — Override throws new unchecked exception type

**Detects:** An override method body contains `throw` statements with exception types that the base method body does not throw (and where the base does not declare these in XML doc `<exception>` tags).

**Why it's an LSP violation:** Caller code only handles exceptions documented or thrown by the base contract. New exception types break the caller's error handling assumptions.

**Implementation note:** Compare the set of `throw new T(...)` types in override vs. base. Static analysis cannot trace exceptions through called methods reliably — limit detection to direct `throw` statements in the method body. This produces some false negatives but no false positives.

---

### LSP004 — Override throws NotImplementedException or NotSupportedException

**Detects:** An override method whose body throws `NotImplementedException` or `NotSupportedException`, especially as the only or first action.

**Why it's an LSP violation:** The override fundamentally cannot fulfill the base contract. This is the textbook "Square inherits from Rectangle but cannot vary width and height independently" pattern.

**Implementation note:** This is a special case of LSP003 but called out separately because the diagnostic message can be more specific and the pattern is so common.

---

### LSP005 — Override narrows parameter type via generic constraint

**Detects:** A generic override method whose type parameter constraints are stricter than the base method's. C# enforces matching constraints in some cases, but generic methods on overridden virtual methods can have constraint mismatches that the compiler does not catch in all configurations.

**Why it's an LSP violation:** The override accepts a smaller set of inputs than the base.

**Implementation note:** This rule has a narrow surface. If during implementation you find C# already prevents all such cases, downgrade this rule to "Info" severity and document why, or remove it and renumber.

---

### LSP006 — Override widens return type contract

**Detects:** An override method whose return type is the same nominal type as the base, but where the override returns values that the base contract did not allow (e.g. base never returns null, override returns null in some path).

**Why it's an LSP violation:** Postcondition weakening — caller relied on a stronger guarantee.

**Implementation note:** Static analysis cannot prove the base never returns null. Limit detection to the case where the override has a `return null` statement and the base method has nullable reference types disabled or the base's return type is non-nullable while the override effectively returns null. Use the Roslyn nullable analysis APIs.

This rule is the weakest of the seven. If it produces too many false positives during testing, consider scoping it more narrowly or removing it.

---

### LSP007 — Override property setter adds validation absent in base

**Detects:** A property setter in a derived class (overriding a virtual property) that contains validation logic (`if/throw`, `ArgumentException`, range checks) which the base setter does not contain.

**Why it's an LSP violation:** This is the shape of the Square/Rectangle problem. Setting `Width` on a `Square` (treated as a `Rectangle`) silently changes `Height` too, or throws — neither matches what the caller expects from `Rectangle`.

**Violating example:**

```csharp
public class Rectangle
{
    public virtual int Width { get; set; }
    public virtual int Height { get; set; }
}

public class Square : Rectangle
{
    public override int Width
    {
        set { base.Width = value; base.Height = value; } // LSP007 (related, narrower variant)
    }
}
```

---

## Implementation order

One rule per day. Implement in this order — earlier rules are simpler and establish the helper patterns later rules reuse.

1. **Day 1:** Project scaffold, `DiagnosticIds`, `DiagnosticDescriptors`, `OverrideAnalysisHelpers`, LSP001 analyzer + tests
2. **Day 2:** `ContractAnalysisHelpers`, LSP002 analyzer + tests
3. **Day 3:** `ExceptionAnalysisHelpers`, LSP003 analyzer + tests
4. **Day 4:** LSP004 analyzer + tests
5. **Day 5:** LSP007 analyzer + tests (skipping ahead — setter analysis is well-bounded)
6. **Day 6:** LSP005 analyzer + tests
7. **Day 7:** LSP006 analyzer + tests, then NuGet packaging and README polish

After day 7, the project has seven working rules, a NuGet package, and a documented README. Future days can add code fixes, more rules, or richer detection.

---

## Day 1 deliverables (specific)

For Claude Code to deliver on day 1:

1. Solution and project structure as listed above, building cleanly
2. `LspGuard.csproj` configured as a Roslyn analyzer (PackageReference to `Microsoft.CodeAnalysis.CSharp` ~ latest stable, `IsRoslynComponent` true, target `netstandard2.0`)
3. `LspGuard.Tests.csproj` with `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit` (or MSTest equivalent), targeting `net10.0`
4. `DiagnosticIds.cs` with all seven IDs as constants
5. `DiagnosticDescriptors.cs` with the LSP001 descriptor (others can be stubs or added per day)
6. `OverrideAnalysisHelpers.cs` with at minimum:
   - `IsOverride(IMethodSymbol method)`
   - `GetOverriddenMethod(IMethodSymbol method)` returning the base `IMethodSymbol`
   - `GetMethodBody(IMethodSymbol method, Compilation compilation)` returning the syntax body or null
7. `PreconditionStrengtheningNullCheckAnalyzer.cs` implementing LSP001 — registers a method symbol or method declaration action, checks override, finds base, compares null-check patterns
8. Tests for LSP001 covering at least:
   - Positive: override adds `ArgumentNullException.ThrowIfNull`, base does not — diagnostic raised
   - Positive: override adds `if (x == null) throw`, base does not — diagnostic raised
   - Negative: base also has the same check — no diagnostic
   - Negative: method is not an override — no diagnostic
   - Negative: override has check but parameter is not from base signature — no diagnostic

---

## Test strategy

Use `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit`. Each analyzer has a corresponding test file with at minimum three positive cases and three negative cases. The verifier framework lets you write expected source with `[|...|]` markers indicating where the diagnostic is expected.

---

## README outline

The README should have, in this order:

- One-paragraph description: what LspGuard is, what LSP is in one line
- Install instructions: `dotnet add package LspGuard`
- Table of supported rules (the seven IDs with one-line descriptions)
- A short section "What this tool does not do" listing the non-goals (no postcondition proofs, no invariant checking)
- A link to the LSP article (placeholder for now)
- License (MIT recommended)

The README does not need to be long. Honesty about limitations is the strongest signal.

---

## Notes for the implementer (Claude Code)

- Do not implement all seven analyzers in one go. Day 1 means day 1.
- Helpers should be `internal static` — no public surface beyond the analyzer attributes.
- All analyzers are sealed classes with `[DiagnosticAnalyzer(LanguageNames.CSharp)]`.
- Prefer `IOperation`-based analysis over raw syntax where possible — it handles language evolution better.
- Tests must run in CI eventually; keep them deterministic and fast.
