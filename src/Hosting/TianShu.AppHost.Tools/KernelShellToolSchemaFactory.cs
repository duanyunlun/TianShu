using System.Text.Json;

namespace TianShu.AppHost.Tools;

/// <summary>
/// 统一构建本地 shell 工具的输入 schema，避免不同宿主入口各自维护一份参数说明。
/// Builds the shared local-shell input schema so different host entry points do not maintain their own parameter descriptions.
/// </summary>
internal static class KernelShellToolSchemaFactory
{
    public static JsonElement BuildShellInputSchema(bool includeLogin, bool includeAdditionalPermissions, bool includeMacOs)
    {
        var properties = new Dictionary<string, object?>
        {
            ["workdir"] = new
            {
                type = "string",
                description = "The working directory to execute the command in",
            },
            ["timeout_ms"] = new
            {
                type = "number",
                description = "The timeout for the command in milliseconds",
            },
        };

        if (includeLogin)
        {
            properties["login"] = new
            {
                type = "boolean",
                description = "Whether to run the shell with login shell semantics. Defaults to the configured allow_login_shell setting.",
            };
        }

        properties["sandbox_permissions"] = new
        {
            type = "string",
            description = includeAdditionalPermissions
                ? "Sandbox permissions for the command. Use \"with_additional_permissions\" to request additional sandboxed filesystem or network permissions (preferred), or \"require_escalated\" to request running without sandbox restrictions; defaults to \"use_default\"."
                : "Sandbox permissions for the command. Set to \"require_escalated\" to request running without sandbox restrictions; defaults to \"use_default\".",
        };
        properties["justification"] = new
        {
            type = "string",
            description = "Only set if sandbox_permissions is \"require_escalated\". Request approval from the user to run this command outside the sandbox. Phrased as a simple question that summarizes the purpose of the command as it relates to the task at hand - e.g. 'Do you want to fetch and pull the latest version of this git branch?'",
        };
        properties["prefix_rule"] = new
        {
            type = "array",
            description = "Only specify when sandbox_permissions is `require_escalated`. Suggest a prefix command pattern that will allow you to fulfill similar requests from the user in the future. Should be a short but reasonable prefix, e.g. [\"git\", \"pull\"] or [\"uv\", \"run\"] or [\"pytest\"].",
            items = new { type = "string" },
        };

        if (includeAdditionalPermissions)
        {
            properties["additional_permissions"] = BuildAdditionalPermissionsSchema(includeMacOs);
        }

        return JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties,
            additionalProperties = false,
        });
    }

    private static object BuildAdditionalPermissionsSchema(bool includeMacOs)
    {
        var properties = new Dictionary<string, object?>
        {
            ["network"] = new
            {
                type = "object",
                additionalProperties = false,
                properties = new Dictionary<string, object?>
                {
                    ["enabled"] = new
                    {
                        type = "boolean",
                        description = "Set to true to enable network access for this command.",
                    },
                },
            },
            ["file_system"] = new
            {
                type = "object",
                additionalProperties = false,
                properties = new Dictionary<string, object?>
                {
                    ["read"] = new
                    {
                        type = "array",
                        description = "Additional filesystem paths to grant read access for this command.",
                        items = new { type = "string" },
                    },
                    ["write"] = new
                    {
                        type = "array",
                        description = "Additional filesystem paths to grant write access for this command.",
                        items = new { type = "string" },
                    },
                },
            },
        };

        if (includeMacOs)
        {
            properties["macos"] = new
            {
                type = "object",
                additionalProperties = false,
                properties = new Dictionary<string, object?>
                {
                    ["preferences"] = new
                    {
                        type = "string",
                        description = "Additional macOS preferences access for this command. Supported values: \"readonly\" or \"readwrite\".",
                    },
                    ["automations"] = new
                    {
                        type = "array",
                        description = "Additional macOS automation targets for this command as bundle IDs.",
                        items = new { type = "string" },
                    },
                    ["accessibility"] = new
                    {
                        type = "boolean",
                        description = "Set to true to allow macOS accessibility APIs for this command.",
                    },
                    ["calendar"] = new
                    {
                        type = "boolean",
                        description = "Set to true to allow macOS Calendar access for this command.",
                    },
                    ["launch_services"] = new
                    {
                        type = "boolean",
                        description = "Set to true to allow macOS Launch Services integration for this command.",
                    },
                    ["reminders"] = new
                    {
                        type = "boolean",
                        description = "Set to true to allow macOS Reminders access for this command.",
                    },
                    ["contacts"] = new
                    {
                        type = "string",
                        description = "Additional macOS Contacts access for this command. Supported values: \"none\", \"read_only\", or \"read_write\".",
                    },
                },
            };
        }

        return new
        {
            type = "object",
            additionalProperties = false,
            properties,
        };
    }
}
