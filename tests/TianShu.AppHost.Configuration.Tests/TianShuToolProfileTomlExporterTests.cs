using TianShu.AppHost.Configuration;
using TianShu.Contracts.Catalog;
using TianShu.Contracts.Tools;

namespace TianShu.AppHost.Configuration.Tests;

public sealed class TianShuToolProfileTomlExporterTests
{
    [Fact]
    public void ExportBuiltinProfileToml_UsesResolvedCatalogItems()
    {
        var toml = TianShuToolProfileTomlExporter.ExportBuiltinProfileToml(
            new ResolvedToolCatalogSnapshot(
            [
                new ResolvedToolCatalogItem(
                    "read_file",
                    "读取文件。",
                    ToolImplementationKind.Managed,
                    available: true,
                    modelVisible: true,
                    implementationId: "read_file"),
                new ResolvedToolCatalogItem(
                    "shell_command",
                    "执行 shell 命令。",
                    ToolImplementationKind.Unavailable,
                    available: false,
                    modelVisible: false,
                    reason: "shell unavailable",
                    implementationId: "shell_command"),
            ]));

        Assert.Contains("[tool_profiles.builtin]", toml, StringComparison.Ordinal);
        Assert.Contains("\"read_file\"", toml, StringComparison.Ordinal);
        Assert.Contains("[tools.read_file]", toml, StringComparison.Ordinal);
        Assert.Contains("implementation_id = \"read_file\"", toml, StringComparison.Ordinal);
        Assert.Contains("implementation_kind = \"managed\"", toml, StringComparison.Ordinal);
        Assert.Contains("[tools.shell_command]", toml, StringComparison.Ordinal);
        Assert.Contains("enabled = false", toml, StringComparison.Ordinal);
        Assert.Contains("# reason = \"shell unavailable\"", toml, StringComparison.Ordinal);
    }
}
