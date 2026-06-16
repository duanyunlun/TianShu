using TianShu.Kernel.Abstractions;

namespace TianShu.Kernel.Validation;

internal static class KernelValidationResults
{
    public static KernelValidationResult Approved()
        => new(KernelValidationDecision.Approved);

    public static KernelValidationResult Rejected(string code, string message, string? sourceRef = null)
        => new(
            KernelValidationDecision.Rejected,
            new[] { new KernelValidationIssue(code, message, KernelValidationIssueSeverity.Error, sourceRef) });
}
