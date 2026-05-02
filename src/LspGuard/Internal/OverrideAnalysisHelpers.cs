using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LspGuard.Internal;

internal static class OverrideAnalysisHelpers
{
    public static bool IsOverride(IMethodSymbol method) => method.IsOverride;

    public static IMethodSymbol? GetOverriddenMethod(IMethodSymbol method) => method.OverriddenMethod;

    public static BlockSyntax? GetMethodBody(IMethodSymbol method, CancellationToken cancellationToken = default)
    {
        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            var node = reference.GetSyntax(cancellationToken);
            if (node is MethodDeclarationSyntax declaration && declaration.Body is { } body)
                return body;
        }
        return null;
    }
}
