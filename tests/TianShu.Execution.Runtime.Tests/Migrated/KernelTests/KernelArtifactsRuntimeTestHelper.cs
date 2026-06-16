using System.Text.Json;

namespace TianShu.Execution.Runtime.Tests;

internal static class KernelArtifactsRuntimeTestHelper
{
    internal static void CreateFakeArtifactRuntime(string tianShuHome)
    {
        var platform = KernelArtifactsRuntimeManager.TryGetPlatformMoniker() ?? throw new InvalidOperationException("unsupported test platform");
        var installDir = Path.Combine(
            tianShuHome,
            "packages",
            "artifacts",
            KernelArtifactsRuntimeManager.PinnedRuntimeVersion,
            platform);
        Directory.CreateDirectory(Path.Combine(installDir, "artifact-tool", "dist"));
        Directory.CreateDirectory(Path.Combine(installDir, "granola-render", "dist"));
        Directory.CreateDirectory(Path.Combine(installDir, "node", "bin"));

        var nodeRelativePath = OperatingSystem.IsWindows() ? "node/bin/node.exe" : "node/bin/node";
        File.WriteAllText(
            Path.Combine(installDir, "manifest.json"),
            JsonSerializer.Serialize(new
            {
                runtime_version = KernelArtifactsRuntimeManager.PinnedRuntimeVersion,
                node = new { relative_path = nodeRelativePath },
                entrypoints = new
                {
                    build_js = new { relative_path = "artifact-tool/dist/artifact_tool.mjs" },
                    render_cli = new { relative_path = "granola-render/dist/render_cli.mjs" },
                },
            }));
        File.WriteAllText(
            Path.Combine(installDir, "artifact-tool", "dist", "artifact_tool.mjs"),
            "export const marker = 'artifact-runtime-ok';\nexport class Presentation {}\nexport class Workbook {}\n");
        File.WriteAllText(
            Path.Combine(installDir, "granola-render", "dist", "render_cli.mjs"),
            "export const ok = true;\n");
    }
}
