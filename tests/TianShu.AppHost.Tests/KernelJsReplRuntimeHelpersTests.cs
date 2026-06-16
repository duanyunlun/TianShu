using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.AppHost.Tests;

public sealed class KernelJsReplRuntimeHelpersTests
{
    [Fact]
    public void ResolveJsReplOptions_ShouldPreferEnvironmentOverrides()
    {
        var options = KernelJsReplRuntimeHelpers.ResolveJsReplOptions(
            cwd: " . ",
            configText: """
                js_repl_node_path = "node-from-config"
                js_repl_node_module_dirs = ["mods-from-config"]
                """,
            environmentNodePath: " node-from-env ",
            environmentModuleDirectories: $"mods-a{Path.PathSeparator}mods-b");

        Assert.Equal("node-from-env", options.NodePath);
        Assert.Equal(".", options.WorkingDirectory);
        Assert.Equal(["mods-a", "mods-b"], options.NodeModuleDirectories);
    }

    [Fact]
    public void ResolveJsReplOptions_ShouldFallbackToConfigValues()
    {
        var options = KernelJsReplRuntimeHelpers.ResolveJsReplOptions(
            cwd: "d:\\work",
            configText: """
                js_repl_node_path = "node-from-config"
                js_repl_node_module_dirs = ["mods-one", "mods-two"]
                """,
            environmentNodePath: null,
            environmentModuleDirectories: null);

        Assert.Equal("node-from-config", options.NodePath);
        Assert.Equal("d:\\work", options.WorkingDirectory);
        Assert.Equal(["mods-one", "mods-two"], options.NodeModuleDirectories);
    }

    [Fact]
    public void BuildJsReplNestedToolCallItemId_ShouldSanitizeUnsafeCharacters()
    {
        var itemId = KernelJsReplRuntimeHelpers.BuildJsReplNestedToolCallItemId(
            "turn_001",
            "request:001",
            "tool/with spaces");

        Assert.Equal("jsrepl_tool_with_spaces_request_001_turn_001", itemId);
    }
}
