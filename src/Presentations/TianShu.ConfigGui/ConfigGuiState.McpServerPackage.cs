namespace TianShu.ConfigGui;

internal sealed partial class ConfigGuiState
{
    private void AddMcpServerPackageNavigationModule()
        => AddNavigationModule(
            "mcp_server_package",
            "MCP服务",
            "管理 modules/mcp-servers/<package>/server.toml server manifest。",
            McpServerPackageCollectionCategoryId);
}
