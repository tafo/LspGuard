using Microsoft.CodeAnalysis;

namespace LspGuard;

internal static class DiagnosticDescriptors
{
    private const string Category = "Design";
    private const string HelpLinkBase = "https://github.com/tafo/LspGuard/blob/main/docs/";

    public static readonly DiagnosticDescriptor PreconditionStrengtheningNullCheck = new(
        id: DiagnosticIds.PreconditionStrengtheningNullCheck,
        title: "Override adds a null check the base does not have",
        messageFormat: "Override of '{0}' rejects null for '{1}' but the base method accepts it",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An override must accept every input the base method accepts. Adding a null check the base does not have breaks Liskov substitution.",
        helpLinkUri: HelpLinkBase + DiagnosticIds.PreconditionStrengtheningNullCheck + ".md");
}
