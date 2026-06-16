using System.Text;
using TianShu.Contracts.Diagnostics;

namespace TianShu.Diagnostics;

/// <summary>
/// 按诊断采集策略过滤 artifact 写入的装饰器。
/// Artifact writer decorator that filters writes by diagnostic collection policy.
/// </summary>
public sealed class FilteringDiagnosticArtifactWriter(
    IDiagnosticArtifactWriter inner,
    IDiagnosticCollectionPolicy policy) : IDiagnosticArtifactWriter
{
    public ValueTask<DiagnosticArtifactManifest> WriteAsync(DiagnosticArtifactWriteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var contentBytes = Encoding.UTF8.GetByteCount(request.Content);
        return policy.ShouldWriteArtifact(request.ArtifactKind, contentBytes, request.Operation, request.Metadata)
            ? inner.WriteAsync(request, cancellationToken)
            : ValueTask.FromResult(new DiagnosticArtifactManifest
            {
                ArtifactId = $"diag-artifact-skipped-{Guid.NewGuid():N}",
                ArtifactKind = request.ArtifactKind,
                FileName = request.FileName,
                RelativePath = string.Empty,
                MediaType = request.MediaType,
                RedactionStatus = "skipped_by_policy",
                Sha256 = string.Empty,
                Bytes = 0,
                SourceEventName = request.SourceEventName,
                Operation = request.Operation,
                Metadata = request.Metadata,
            });
    }
}
