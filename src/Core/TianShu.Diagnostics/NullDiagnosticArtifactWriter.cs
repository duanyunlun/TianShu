using TianShu.Contracts.Diagnostics;

namespace TianShu.Diagnostics;

/// <summary>
/// 空诊断产物写入器，用于 manifest 禁用 artifact sink 时保持调用链可运行。
/// Null diagnostic artifact writer used when artifact sinks are disabled.
/// </summary>
public sealed class NullDiagnosticArtifactWriter : IDiagnosticArtifactWriter
{
    public ValueTask<DiagnosticArtifactManifest> WriteAsync(DiagnosticArtifactWriteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ValueTask.FromResult(new DiagnosticArtifactManifest
        {
            ArtifactId = $"diag-artifact-disabled-{Guid.NewGuid():N}",
            ArtifactKind = request.ArtifactKind,
            FileName = request.FileName,
            RelativePath = string.Empty,
            MediaType = request.MediaType,
            RedactionStatus = "disabled_by_sink_manifest",
            Sha256 = string.Empty,
            Bytes = 0,
            SourceEventName = request.SourceEventName,
            Operation = request.Operation,
            Metadata = request.Metadata,
        });
    }
}
