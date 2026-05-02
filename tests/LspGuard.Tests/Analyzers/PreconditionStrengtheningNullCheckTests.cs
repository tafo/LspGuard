using LspGuard.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace LspGuard.Tests.Analyzers;

public class PreconditionStrengtheningNullCheckTests
{
    [Fact]
    public async Task OverrideAddsThrowIfNull_BaseDoesNot_Reports()
    {
        const string source = """
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
            """;

        var expected = new DiagnosticResult("LSP001", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("Log", "message");

        await new CSharpAnalyzerTest<PreconditionStrengtheningNullCheckAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ExpectedDiagnostics = { expected },
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        }.RunAsync();
    }
}
