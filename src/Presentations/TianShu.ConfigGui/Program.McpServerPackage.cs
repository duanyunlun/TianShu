using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using TianShu.Configuration;
using TianShu.Contracts.Configuration;
using TianShu.Contracts.Primitives;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TianShu.ConfigGui;

internal static partial class Program
{
    private static Border BuildMcpServerPackageCollectionPanel()
    {
        var packageHeader = new Grid()
            .DockTop()
            .Columns("*,Auto")
            .Rows("Auto,Auto,Auto")
            .Spacing(8)
            .Children(
                new StackPanel()
                    .Vertical()
                    .Spacing(4)
                    .Children(
                        new TextBlock().Text("MCP Server 包").Bold().FontSize(18),
                        new TextBlock().BindText(State.McpServerPackageRootText).FontSize(11).TextWrapping(TextWrapping.Wrap)),
                new ComboBox()
                    .Row(1)
                    .Column(0)
                    .Ref(out mcpServerPackageComboBox)
                    .Height(EditControlHeight)
                    .StableItems(State.McpServerPackageLabels.ToArray())
                    .BindSelectedIndex(State.McpServerPackageIndex)
                    .OnSelectionChanged(_ => ApplyCollectionSelection(
                        () => State.SelectMcpServerPackageIndex(State.McpServerPackageIndex.Value),
                        RefreshMcpServerPackageControls)),
                new StackPanel()
                    .Row(1)
                    .Column(1)
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        new Button().Content("新建").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.BeginNewMcpServerPackage, RefreshMcpServerPackageControls)),
                        new Button().Content("复制").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.CopySelectedMcpServerPackageToDraft, RefreshMcpServerPackageControls)),
                        new Button().Content("删除").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.DeleteSelectedMcpServerPackage, RefreshViewControls))),
                new TextBlock().Row(2).ColumnSpan(2).BindText(State.McpServerPackageManifestPathText).FontSize(11).TextWrapping(TextWrapping.Wrap));

        var packageEditor = new Grid()
            .DockTop()
            .Columns("*,300")
            .Spacing(24)
            .Children(
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock().Text("Server 包基本信息").Bold().FontSize(13),
                        new Grid()
                            .Columns("140,*")
                            .Rows("Auto,Auto,Auto")
                            .Spacing(10)
                            .Children(
                                new TextBlock().Text("包 ID").Bold().CenterVertical(),
                                new TextBox().Column(1).BindText(State.McpServerPackageId).Placeholder("例如 company-mcp"),
                                new TextBlock().Row(1).Text("显示名").Bold().CenterVertical(),
                                new TextBox().Row(1).Column(1).BindText(State.McpServerPackageDisplayName).Placeholder("显示名称"),
                                new TextBlock().Row(2).Text("优先级").Bold().CenterVertical(),
                                new TextBox().Row(2).Column(1).BindText(State.McpServerPackagePriority).Placeholder("0"))),
                new StackPanel()
                    .Column(1)
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock().Text("Server 包加载设置").Bold().FontSize(13),
                        new Grid()
                            .Columns("120,*")
                            .Rows("Auto,Auto")
                            .Spacing(10)
                            .Children(
                                new TextBlock().Text("启用状态").Bold().CenterVertical(),
                                new ComboBox()
                                    .Column(1)
                                    .Ref(out mcpServerPackageEnabledComboBox)
                                    .StableItems(State.ToolBooleanLabels.ToArray())
                                    .BindSelectedIndex(State.McpServerPackageEnabledIndex),
                                new TextBlock().Row(1).Text("类型").Bold().CenterVertical(),
                                new ComboBox()
                                    .Row(1)
                                    .Column(1)
                                    .Ref(out mcpServerPackageTypeComboBox)
                                    .StableItems(State.McpServerPackageTypeLabels.ToArray())
                                    .BindSelectedIndex(State.McpServerPackageTypeIndex))));

        var serverHeader = new Grid()
            .DockTop()
            .Columns("*,Auto")
            .Rows("Auto,Auto")
            .Spacing(8)
            .Children(
                new TextBlock().Text("Server 条目").Bold().FontSize(15),
                new ComboBox()
                    .Row(1)
                    .Column(0)
                    .Ref(out mcpServerComboBox)
                    .Height(EditControlHeight)
                    .StableItems(State.McpServerLabels.ToArray())
                    .BindSelectedIndex(State.McpServerIndex)
                    .OnSelectionChanged(_ => ApplyCollectionSelection(
                        () => State.SelectMcpServerIndex(State.McpServerIndex.Value),
                        RefreshMcpServerPackageControls)),
                new StackPanel()
                    .Row(1)
                    .Column(1)
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        new Button().Content("新增 Server").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.BeginNewMcpServer, RefreshMcpServerPackageControls)),
                        new Button().Content("删除 Server").Height(EditControlHeight).OnClick(() => RunCollectionCommand(State.DeleteSelectedMcpServer, RefreshMcpServerPackageControls))));

        var serverEditor = new Grid()
            .DockTop()
            .Columns("*,300")
            .Spacing(24)
            .Children(
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock().Text("Server 基本信息").Bold().FontSize(13),
                        new Grid()
                            .Columns("140,*")
                            .Rows("Auto,Auto,Auto,Auto,Auto,Auto,Auto")
                            .Spacing(10)
                            .Children(
                                new TextBlock().Text("Server ID").Bold().CenterVertical(),
                                new TextBox().Column(1).BindText(State.McpServerId).Placeholder("例如 docs"),
                                new TextBlock().Row(1).Text("显示名").Bold().CenterVertical(),
                                new TextBox().Row(1).Column(1).BindText(State.McpServerDisplayName).Placeholder("显示名称"),
                                new TextBlock().Row(2).Text("传输").Bold().CenterVertical(),
                                new ComboBox()
                                    .Row(2)
                                    .Column(1)
                                    .Ref(out mcpServerTransportComboBox)
                                    .StableItems(State.McpServerTransportLabels.ToArray())
                                    .BindSelectedIndex(State.McpServerTransportIndex),
                                new TextBlock().Row(3).Text("命令").Bold().CenterVertical(),
                                new TextBox().Row(3).Column(1).BindText(State.McpServerCommand).Placeholder("npx"),
                                new TextBlock().Row(4).Text("参数").Bold().CenterVertical(),
                                new TextBox().Row(4).Column(1).BindText(State.McpServerArgs).Placeholder("-y, @modelcontextprotocol/server-example"),
                                new TextBlock().Row(5).Text("工作目录").Bold().CenterVertical(),
                                new TextBox().Row(5).Column(1).BindText(State.McpServerCwd).Placeholder("相对 server.toml 目录"),
                                new TextBlock().Row(6).Text("HTTP URL").Bold().CenterVertical(),
                                new TextBox().Row(6).Column(1).BindText(State.McpServerUrl).Placeholder("https://example.com/mcp"))),
                new StackPanel()
                    .Column(1)
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock().Text("Server 运行设置").Bold().FontSize(13),
                        new Grid()
                            .Columns("120,*")
                            .Rows("Auto,Auto,Auto,Auto,Auto,Auto")
                            .Spacing(10)
                            .Children(
                                new TextBlock().Text("启用状态").Bold().CenterVertical(),
                                new ComboBox()
                                    .Column(1)
                                    .Ref(out mcpServerEnabledComboBox)
                                    .StableItems(State.ToolBooleanLabels.ToArray())
                                    .BindSelectedIndex(State.McpServerEnabledIndex),
                                new TextBlock().Row(1).Text("必需启动").Bold().CenterVertical(),
                                new ComboBox()
                                    .Row(1)
                                    .Column(1)
                                    .Ref(out mcpServerRequiredComboBox)
                                    .StableItems(State.McpServerRequiredLabels.ToArray())
                                    .BindSelectedIndex(State.McpServerRequiredIndex),
                                new TextBlock().Row(2).Text("Token Env").Bold().CenterVertical(),
                                new TextBox().Row(2).Column(1).BindText(State.McpServerBearerTokenEnvVar).Placeholder("DOCS_MCP_TOKEN"),
                                new TextBlock().Row(3).Text("透传 Env").Bold().CenterVertical(),
                                new TextBox().Row(3).Column(1).BindText(State.McpServerEnvVars).Placeholder("A, B"),
                                new TextBlock().Row(4).Text("启动超时 ms").Bold().CenterVertical(),
                                new TextBox().Row(4).Column(1).BindText(State.McpServerStartupTimeoutMs).Placeholder("10000"),
                                new TextBlock().Row(5).Text("工具超时 ms").Bold().CenterVertical(),
                                new TextBox().Row(5).Column(1).BindText(State.McpServerToolTimeoutMs).Placeholder("120000"))));

        var filtersEditor = new Grid()
            .DockTop()
            .Columns("*,*")
            .Spacing(16)
            .Children(
                new StackPanel()
                    .Vertical()
                    .Spacing(6)
                    .Children(
                        new TextBlock().Text("启用工具列表").Bold().FontSize(13),
                        new TextBox().BindText(State.McpServerEnabledTools).Placeholder("tool_a, tool_b")),
                new StackPanel()
                    .Column(1)
                    .Vertical()
                    .Spacing(6)
                    .Children(
                        new TextBlock().Text("禁用工具列表").Bold().FontSize(13),
                        new TextBox().BindText(State.McpServerDisabledTools).Placeholder("tool_x, tool_y")));

        var content = new DockPanel()
            .Spacing(10)
            .Children(
                packageHeader,
                packageEditor,
                new StackPanel()
                    .DockTop()
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        new Button().Content("保存 MCP Server 包").OnClick(() => RunCollectionCommand(State.SaveMcpServerPackage, RefreshViewControls))),
                serverHeader,
                serverEditor,
                filtersEditor,
                new TextBlock().DockTop().BindText(State.McpServerResolvedPathText).FontSize(11).TextWrapping(TextWrapping.Wrap),
                new TextBlock().DockTop().BindText(State.McpServerPackageStatusText).FontSize(11).TextWrapping(TextWrapping.Wrap));

        return new Border()
            .Padding(12)
            .BorderThickness(1)
            .CornerRadius(8)
            .WithTheme((theme, border) => border.Background(theme.Palette.ContainerBackground).BorderBrush(theme.Palette.ControlBorder))
            .Child(content);
    }
}
