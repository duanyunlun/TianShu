using TianShu.Contracts.Configuration;

namespace TianShu.ConfigGui;

internal sealed partial class ConfigGuiState
{
    private void AddDiagnosticSinkPackageNavigationModule()
        => AddNavigationModule(
            "diagnostic_sink_package",
            "诊断输出",
            "管理 modules/diagnostics/sinks/<package>/sink.toml diagnostics / telemetry sink manifest。",
            ConfigurationCategoryIds.DiagnosticsState,
            DiagnosticSinkPackageCollectionCategoryId);
}
