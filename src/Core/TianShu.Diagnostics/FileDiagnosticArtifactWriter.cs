using System.Security.Cryptography;
using System.Text;
using TianShu.Contracts.Diagnostics;

namespace TianShu.Diagnostics;

/// <summary>
/// 文件诊断产物写入器，只写入经过脱敏的内容。
/// File-based diagnostic artifact writer that persists sanitized content only.
/// </summary>
public sealed class FileDiagnosticArtifactWriter : IDiagnosticArtifactWriter
{
    private readonly string rootDirectory;
    private readonly IDiagnosticRedactor redactor;
    private readonly Func<DateTimeOffset> utcNow;

    public FileDiagnosticArtifactWriter(
        string rootDirectory,
        IDiagnosticRedactor? redactor = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        this.rootDirectory = Path.GetFullPath(rootDirectory);
        this.redactor = redactor ?? new DefaultDiagnosticRedactor();
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async ValueTask<DiagnosticArtifactManifest> WriteAsync(DiagnosticArtifactWriteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ArtifactKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MediaType);

        Directory.CreateDirectory(rootDirectory);

        var safeFileName = Path.GetFileName(request.FileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            safeFileName = $"diagnostic-artifact-{Guid.NewGuid():N}.txt";
        }

        var sanitizedContent = request.ContentAlreadySanitized
            ? request.Content
            : redactor.RedactText(null, request.Content);
        var bytes = Encoding.UTF8.GetBytes(sanitizedContent);
        var fullPath = Path.Combine(rootDirectory, safeFileName);
        await File.WriteAllBytesAsync(fullPath, bytes, cancellationToken).ConfigureAwait(false);

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new DiagnosticArtifactManifest
        {
            ArtifactId = $"diag-artifact-{Guid.NewGuid():N}",
            ArtifactKind = request.ArtifactKind,
            FileName = safeFileName,
            RelativePath = safeFileName,
            MediaType = request.MediaType,
            RedactionStatus = request.ContentAlreadySanitized ? "already_sanitized" : "sanitized",
            Sha256 = hash,
            Bytes = bytes.LongLength,
            CreatedAt = utcNow(),
            SourceEventName = request.SourceEventName,
            Operation = request.Operation,
            Metadata = request.Metadata,
        };
    }
}
