using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace TianShu.ArchitectureAnalyzers;

/// <summary>
/// 约束诊断代码之外不得直接读取 <c>AgentStreamEvent</c> 的原始 JSON 字段。
/// Prevents non-diagnostics code from reading raw <c>AgentStreamEvent</c> JSON fields directly.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AgentStreamEventDiagnosticsJsonAccessAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "OA0001";

    private const string AgentStreamEventMetadataName = "global::TianShu.Execution.Runtime.Events.AgentStreamEvent";
    private const string DiagnosticsJsonAccessAllowedAttributeMetadataName = "global::TianShu.Execution.Runtime.Diagnostics.DiagnosticsJsonAccessAllowedAttribute";

    private static readonly ImmutableHashSet<string> RestrictedPropertyNames =
        ImmutableHashSet.Create(StringComparer.Ordinal, "DataJson", "MetadataJson", "RawJson");

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "禁止在业务链路中直接读取 AgentStreamEvent 原始 JSON 字段",
        "AgentStreamEvent.{0} 仅允许在标记了 [DiagnosticsJsonAccessAllowed] 的诊断代码中访问；业务链路必须消费 typed payload。",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "RawJson, DataJson, and MetadataJson are reserved for diagnostics only. Presentation business flows must consume typed payloads.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzePropertyReference, OperationKind.PropertyReference);
    }

    private static void AnalyzePropertyReference(OperationAnalysisContext context)
    {
        if (context.Operation is not IPropertyReferenceOperation propertyReference)
        {
            return;
        }

        var property = propertyReference.Property;
        if (!RestrictedPropertyNames.Contains(property.Name)
            || !string.Equals(
                property.ContainingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                AgentStreamEventMetadataName,
                StringComparison.Ordinal))
        {
            return;
        }

        if (IsAllowedInCurrentScope(context.ContainingSymbol))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, propertyReference.Syntax.GetLocation(), property.Name));
    }

    private static bool IsAllowedInCurrentScope(ISymbol? symbol)
    {
        for (var current = symbol; current is not null; current = current.ContainingSymbol)
        {
            if (current.GetAttributes().Any(static attribute =>
                    string.Equals(
                        attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        DiagnosticsJsonAccessAllowedAttributeMetadataName,
                        StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }
}
