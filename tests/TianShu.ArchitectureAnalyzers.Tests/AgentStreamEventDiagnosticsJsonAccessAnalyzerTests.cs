using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using TianShu.ArchitectureAnalyzers;
using TianShu.Execution.Runtime.Diagnostics;
using TianShu.Execution.Runtime.Events;
using Xunit;

namespace TianShu.ArchitectureAnalyzers.Tests;

public sealed class AgentStreamEventDiagnosticsJsonAccessAnalyzerTests
{
    [Fact]
    public async Task AnalyzeAsync_WhenDirectlyReadingDiagnosticsJson_ReportsDiagnostic()
    {
        const string source =
            """
            using TianShu.Execution.Runtime.Events;

            namespace Demo;

            internal static class Consumer
            {
                internal static string? Read(AgentStreamEvent streamEvent)
                    => streamEvent.RawJson + streamEvent.DataJson + streamEvent.MetadataJson;
            }
            """;

        var diagnostics = await AnalyzeAsync(source);

        Assert.Collection(
            diagnostics.OrderBy(static item => item.Location.SourceSpan.Start),
            diagnostic => AssertDiagnostic(diagnostic, "RawJson"),
            diagnostic => AssertDiagnostic(diagnostic, "DataJson"),
            diagnostic => AssertDiagnostic(diagnostic, "MetadataJson"));
    }

    [Fact]
    public async Task AnalyzeAsync_WhenAccessOccursInsideAllowedMethod_DoesNotReportDiagnostic()
    {
        const string source =
            """
            using TianShu.Execution.Runtime.Diagnostics;
            using TianShu.Execution.Runtime.Events;

            namespace Demo;

            internal static class Consumer
            {
                [DiagnosticsJsonAccessAllowed]
                internal static string? Read(AgentStreamEvent streamEvent)
                    => streamEvent.RawJson;
            }
            """;

        var diagnostics = await AnalyzeAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenOnlyReadingTypedPayload_DoesNotReportDiagnostic()
    {
        const string source =
            """
            using TianShu.Execution.Runtime.Events;

            namespace Demo;

            internal static class Consumer
            {
                internal static string? Read(AgentStreamEvent streamEvent)
                    => streamEvent.Text;
            }
            """;

        var diagnostics = await AnalyzeAsync(source);

        Assert.Empty(diagnostics);
    }

    private static void AssertDiagnostic(Diagnostic diagnostic, string propertyName)
    {
        Assert.Equal(AgentStreamEventDiagnosticsJsonAccessAnalyzer.DiagnosticId, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains($"AgentStreamEvent.{propertyName}", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    private static async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            assemblyName: "TianShu.ArchitectureAnalyzers.Tests.Dynamic",
            syntaxTrees: [syntaxTree],
            references: BuildMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var compilationErrors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.True(
            compilationErrors.Length == 0,
            "测试输入编译失败：" + Environment.NewLine + string.Join(Environment.NewLine, compilationErrors.Select(static diagnostic => diagnostic.ToString())));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new AgentStreamEventDiagnosticsJsonAccessAnalyzer());
        var diagnostics = await compilation
            .WithAnalyzers(analyzers)
            .GetAnalyzerDiagnosticsAsync()
            .ConfigureAwait(false);
        return diagnostics;
    }

    private static IReadOnlyList<MetadataReference> BuildMetadataReferences()
    {
        var references = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (!string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
        {
            foreach (var path in trustedPlatformAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                references[path] = MetadataReference.CreateFromFile(path);
            }
        }

        foreach (var assembly in new[]
                 {
                     typeof(object).Assembly,
                     typeof(Enumerable).Assembly,
                     typeof(AgentStreamEvent).Assembly,
                     typeof(DiagnosticsJsonAccessAllowedAttribute).Assembly,
                 })
        {
            references[assembly.Location] = MetadataReference.CreateFromFile(assembly.Location);
        }

        return references.Values.ToArray();
    }
}
