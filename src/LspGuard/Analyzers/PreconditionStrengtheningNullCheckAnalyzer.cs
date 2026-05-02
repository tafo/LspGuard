using System.Collections.Immutable;
using LspGuard.Internal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LspGuard.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PreconditionStrengtheningNullCheckAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [DiagnosticDescriptors.PreconditionStrengtheningNullCheck];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var declaration = (MethodDeclarationSyntax)context.Node;
        if (declaration.Body is null)
            return;

        if (context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is not { } method)
            return;

        if (!OverrideAnalysisHelpers.IsOverride(method))
            return;

        var baseMethod = OverrideAnalysisHelpers.GetOverriddenMethod(method);
        if (baseMethod is null)
            return;

        var baseBody = OverrideAnalysisHelpers.GetMethodBody(baseMethod, context.CancellationToken);

        foreach (var parameter in method.Parameters)
        {
            if (parameter.Type.IsValueType && parameter.Type.NullableAnnotation != NullableAnnotation.Annotated)
                continue;

            var overrideHasCheck = HasNullCheckForParameter(declaration.Body, parameter.Name);
            if (!overrideHasCheck)
                continue;

            var baseHasCheck = baseBody is not null && HasNullCheckForParameter(baseBody, baseMethod.Parameters[parameter.Ordinal].Name);
            if (baseHasCheck)
                continue;

            var location = overrideHasCheck.Location ?? declaration.Identifier.GetLocation();
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.PreconditionStrengtheningNullCheck,
                location,
                method.Name,
                parameter.Name));
        }
    }

    private readonly struct NullCheckMatch(Location? location)
    {
        public Location? Location { get; } = location;
        private bool HasMatch { get; } = true;
        public static implicit operator bool(NullCheckMatch m) => m.HasMatch;
    }

    private static NullCheckMatch HasNullCheckForParameter(BlockSyntax body, string parameterName)
    {
        foreach (var node in body.DescendantNodes())
        {
            switch (node)
            {
                case InvocationExpressionSyntax invocation when IsThrowIfNullFor(invocation, parameterName):
                    return new NullCheckMatch(invocation.GetLocation());
                case IfStatementSyntax ifStatement when IsNullThrowFor(ifStatement, parameterName):
                    return new NullCheckMatch(ifStatement.GetLocation());
                case ThrowExpressionSyntax throwExpr when IsArgumentNullThrowFor(throwExpr, parameterName):
                    return new NullCheckMatch(throwExpr.GetLocation());
            }
        }
        return default;
    }

    private static bool IsThrowIfNullFor(InvocationExpressionSyntax invocation, string parameterName)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax member)
            return false;
        if (member.Name.Identifier.Text != "ThrowIfNull")
            return false;
        if (member.Expression is not IdentifierNameSyntax type || type.Identifier.Text != "ArgumentNullException")
            return false;
        var args = invocation.ArgumentList.Arguments;
        if (args.Count == 0)
            return false;
        return args[0].Expression is IdentifierNameSyntax id && id.Identifier.Text == parameterName;
    }

    private static bool IsNullThrowFor(IfStatementSyntax ifStatement, string parameterName)
    {
        return ConditionIsNullCheck(ifStatement.Condition, parameterName) && StatementThrows(ifStatement.Statement);
    }

    private static bool ConditionIsNullCheck(ExpressionSyntax condition, string parameterName)
    {
        return condition switch
        {
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.EqualsExpression) => IsParameterAndNull(
                binary.Left, binary.Right, parameterName),
            IsPatternExpressionSyntax pattern when pattern.Expression is IdentifierNameSyntax id &&
                                                   id.Identifier.Text == parameterName =>
                pattern.Pattern is ConstantPatternSyntax constant &&
                constant.Expression.IsKind(SyntaxKind.NullLiteralExpression),
            _ => false
        };
    }

    private static bool IsParameterAndNull(ExpressionSyntax left, ExpressionSyntax right, string parameterName)
    {
        return (left is IdentifierNameSyntax l && l.Identifier.Text == parameterName && right.IsKind(SyntaxKind.NullLiteralExpression))
            || (right is IdentifierNameSyntax r && r.Identifier.Text == parameterName && left.IsKind(SyntaxKind.NullLiteralExpression));
    }

    private static bool StatementThrows(StatementSyntax statement)
    {
        return statement switch
        {
            ThrowStatementSyntax => true,
            BlockSyntax block => block.Statements.Count > 0 && block.Statements[0] is ThrowStatementSyntax,
            _ => false,
        };
    }

    private static bool IsArgumentNullThrowFor(ThrowExpressionSyntax throwExpr, string parameterName)
    {
        if (throwExpr.Expression is not ObjectCreationExpressionSyntax creation)
            return false;
        if (creation.Type is not IdentifierNameSyntax type || type.Identifier.Text != "ArgumentNullException")
            return false;
        return creation.ArgumentList?.Arguments.Count > 0
            && creation.ArgumentList.Arguments[0].Expression is InvocationExpressionSyntax inv
            && inv.Expression is IdentifierNameSyntax nameOf && nameOf.Identifier.Text == "nameof"
            && inv.ArgumentList.Arguments.Count > 0
            && inv.ArgumentList.Arguments[0].Expression is IdentifierNameSyntax target
            && target.Identifier.Text == parameterName;
    }
}