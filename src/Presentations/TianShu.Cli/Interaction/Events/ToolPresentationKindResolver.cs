using TianShu.Cli.Interaction;

namespace TianShu.Cli.Interaction.Events;

internal static class ToolPresentationKindResolver
{
    public static ToolPresentationKind ResolveFallbackFromToolName(string toolName)
    {
        if (IsShellTool(toolName))
        {
            return ToolPresentationKind.Command;
        }

        if (string.Equals(toolName, "apply_patch", StringComparison.OrdinalIgnoreCase))
        {
            return ToolPresentationKind.CodePatch;
        }

        if (string.Equals(toolName, "write", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "edit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "fileChange", StringComparison.OrdinalIgnoreCase))
        {
            return ToolPresentationKind.FileChange;
        }

        if (string.Equals(toolName, "update_plan", StringComparison.OrdinalIgnoreCase))
        {
            return ToolPresentationKind.PlanUpdate;
        }

        if (string.Equals(toolName, "webSearch", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "web_search_call", StringComparison.OrdinalIgnoreCase))
        {
            return ToolPresentationKind.WebSearch;
        }

        if (string.Equals(toolName, "imageGeneration", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "image_generation_call", StringComparison.OrdinalIgnoreCase))
        {
            return ToolPresentationKind.ImageGeneration;
        }

        if (string.Equals(toolName, "imageView", StringComparison.OrdinalIgnoreCase))
        {
            return ToolPresentationKind.ImageView;
        }

        if (string.Equals(toolName, "toolSearch", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "tool_search_call", StringComparison.OrdinalIgnoreCase))
        {
            return ToolPresentationKind.Search;
        }

        return ToolPresentationKind.Generic;
    }

    public static ToolPresentationKind? ResolveFromMetadata(string? value)
    {
        var normalized = Normalize(value);
        if (normalized is null)
        {
            return null;
        }

        return normalized switch
        {
            "command" or "shell" or "local_shell" or "shellcommand" or "shell_command" or "commandexecution" => ToolPresentationKind.Command,
            "codepatch" or "code_patch" or "patch" or "apply_patch" => ToolPresentationKind.CodePatch,
            "file" or "filechange" or "file_change" or "writefile" or "write_file" or "editfile" or "edit_file" => ToolPresentationKind.FileChange,
            "plan" or "planupdate" or "plan_update" or "update_plan" => ToolPresentationKind.PlanUpdate,
            "web" or "websearch" or "web_search" or "websearchcall" or "web_search_call" => ToolPresentationKind.WebSearch,
            "imagegeneration" or "image_generation" or "imagegenerationcall" or "image_generation_call" => ToolPresentationKind.ImageGeneration,
            "imageview" or "image_view" => ToolPresentationKind.ImageView,
            "search" or "toolsearch" or "tool_search" or "toolsearchcall" or "tool_search_call" => ToolPresentationKind.Search,
            "generic" or "tool" => ToolPresentationKind.Generic,
            _ => null,
        };
    }

    private static bool IsShellTool(string toolName)
        => string.Equals(toolName, "shell", StringComparison.OrdinalIgnoreCase)
           || string.Equals(toolName, "local_shell", StringComparison.OrdinalIgnoreCase)
           || string.Equals(toolName, "container.exec", StringComparison.OrdinalIgnoreCase)
           || string.Equals(toolName, "shell_command", StringComparison.OrdinalIgnoreCase)
           || string.Equals(toolName, "exec_command", StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().Replace("-", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
}
