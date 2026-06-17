using TianShu.Contracts.Kernel;

namespace TianShu.Kernel.Abstractions;

/// <summary>
/// Kernel 客观锚点校准服务，用 build/test/golden answer/human label 校准模型裁判可信度。
/// Kernel objective-anchor calibration service that calibrates model-judge credibility with build/test/golden-answer/human-label anchors.
/// </summary>
public interface IKernelObjectiveAnchorCalibrationService
{
    Task<KernelObjectiveAnchorCalibrationReport> CalibrateAsync(
        KernelObjectiveAnchorCalibrationRequest request,
        CancellationToken cancellationToken = default);
}
