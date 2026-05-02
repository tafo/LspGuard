using LspGuard.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace LspGuard.Tests.Analyzers;

public class PreconditionStrengtheningNullCheckTests
{
    private static Task VerifyAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<PreconditionStrengtheningNullCheckAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync();
    }

    private static DiagnosticResult Expect(string method, string parameter) =>
        new DiagnosticResult("LSP001", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments(method, parameter);

    [Fact]
    public Task OverrideAddsThrowIfNull_BaseDoesNot_Reports() =>
        VerifyAsync("""
            using System;

            public class Logger
            {
                public virtual void Log(string message) { }
            }

            public class StrictLogger : Logger
            {
                public override void Log(string message)
                {
                    {|#0:ArgumentNullException.ThrowIfNull(message)|};
                }
            }
            """, Expect("Log", "message"));

    [Fact]
    public Task OverrideAddsIfNullThrow_BaseDoesNot_Reports() =>
        VerifyAsync("""
            using System;

            public class Logger
            {
                public virtual void Log(string message) { }
            }

            public class StrictLogger : Logger
            {
                public override void Log(string message)
                {
                    {|#0:if (message == null) throw new ArgumentNullException(nameof(message));|}
                }
            }
            """, Expect("Log", "message"));

    [Fact]
    public Task OverrideAddsIsNullThrow_BaseDoesNot_Reports() =>
        VerifyAsync("""
            using System;

            public class Logger
            {
                public virtual void Log(string message) { }
            }

            public class StrictLogger : Logger
            {
                public override void Log(string message)
                {
                    {|#0:if (message is null) throw new ArgumentNullException(nameof(message));|}
                }
            }
            """, Expect("Log", "message"));

    [Fact]
    public Task BaseAlsoHasNullCheck_NoDiagnostic() =>
        VerifyAsync("""
            using System;

            public class Logger
            {
                public virtual void Log(string message)
                {
                    ArgumentNullException.ThrowIfNull(message);
                }
            }

            public class StrictLogger : Logger
            {
                public override void Log(string message)
                {
                    ArgumentNullException.ThrowIfNull(message);
                }
            }
            """);

    [Fact]
    public Task NotAnOverride_NoDiagnostic() =>
        VerifyAsync("""
            using System;

            public class Logger
            {
                public void Log(string message)
                {
                    ArgumentNullException.ThrowIfNull(message);
                }
            }
            """);

    [Fact]
    public Task NullCheckOnLocal_NoDiagnostic() =>
        VerifyAsync("""
            using System;

            public class Logger
            {
                public virtual void Log(string message) { }
            }

            public class StrictLogger : Logger
            {
                public override void Log(string message)
                {
                    string local = "x";
                    ArgumentNullException.ThrowIfNull(local);
                }
            }
            """);

    [Fact]
    public Task NonNullGuard_NoDiagnostic() =>
        VerifyAsync("""
            public class Logger
            {
                public virtual void Log(string message) { }
            }

            public class StrictLogger : Logger
            {
                public override void Log(string message)
                {
                    if (message != null) System.Console.WriteLine(message);
                }
            }
            """);

    [Fact]
    public Task ParameterRenamedInOverride_StillDetected() =>
        VerifyAsync("""
            using System;

            public class Logger
            {
                public virtual void Log(string message) { }
            }

            public class StrictLogger : Logger
            {
                public override void Log(string text)
                {
                    {|#0:ArgumentNullException.ThrowIfNull(text)|};
                }
            }
            """, Expect("Log", "text"));

    [Fact]
    public Task MultipleParameters_OnlyCheckedOneReported() =>
        VerifyAsync("""
            using System;

            public class Logger
            {
                public virtual void Log(string tag, string message) { }
            }

            public class StrictLogger : Logger
            {
                public override void Log(string tag, string message)
                {
                    {|#0:ArgumentNullException.ThrowIfNull(message)|};
                }
            }
            """, Expect("Log", "message"));

    [Fact]
    public Task ThrowExpressionForm_Reports() =>
        VerifyAsync("""
            using System;

            public class Logger
            {
                public virtual void Log(string message) { }
            }

            public class StrictLogger : Logger
            {
                public override void Log(string message)
                {
                    var safe = message ?? {|#0:throw new ArgumentNullException(nameof(message))|};
                }
            }
            """, Expect("Log", "message"));

    [Fact]
    public Task BaseHasIfThrow_OverrideUsesThrowIfNull_NoDiagnostic() =>
        VerifyAsync("""
            using System;

            public class Logger
            {
                public virtual void Log(string message)
                {
                    if (message is null) throw new ArgumentNullException(nameof(message));
                }
            }

            public class StrictLogger : Logger
            {
                public override void Log(string message)
                {
                    ArgumentNullException.ThrowIfNull(message);
                }
            }
            """);
}
