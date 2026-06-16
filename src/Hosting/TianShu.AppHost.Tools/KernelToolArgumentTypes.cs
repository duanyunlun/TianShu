using System.Text.Json;
using System.Text.Json.Serialization;

namespace TianShu.AppHost.Tools;

/// <summary>
/// 宿主内置 shell 工具的可见形态。
/// Visible shell tool shape exposed by the host tool surface.
/// </summary>
internal enum KernelShellToolType
{
    Disabled = 0,
    Default = 1,
    ShellCommand = 2,
    UnifiedExec = 3,
    Local = 4,
}

/// <summary>
/// shell / local_shell 的严格参数模型。
/// Strict argument contract for shell / local_shell.
/// </summary>
internal sealed class KernelShellToolCallArguments
{
    [JsonPropertyName("command")]
    [JsonConverter(typeof(KernelStringArrayOrStringifiedJsonArrayConverter))]
    public string[]? Command { get; init; }

    [JsonPropertyName("workdir")]
    public string? Workdir { get; init; }

    [JsonPropertyName("timeout_ms")]
    public int? TimeoutMs { get; init; }

    [JsonPropertyName("timeout")]
    public int? TimeoutAlias { get; init; }

    [JsonPropertyName("sandbox_permissions")]
    public string? SandboxPermissions { get; init; }

    [JsonPropertyName("prefix_rule")]
    public string[]? PrefixRule { get; init; }

    [JsonPropertyName("additional_permissions")]
    public KernelAdditionalPermissionArguments? AdditionalPermissions { get; init; }

    [JsonPropertyName("justification")]
    public string? Justification { get; init; }

    public int? ResolveTimeoutMs()
        => TimeoutMs ?? TimeoutAlias;
}

/// <summary>
/// shell_command 的严格参数模型。
/// Strict argument contract for shell_command.
/// </summary>
internal sealed class KernelShellCommandToolCallArguments
{
    [JsonPropertyName("command")]
    public string? Command { get; init; }

    [JsonPropertyName("workdir")]
    public string? Workdir { get; init; }

    [JsonPropertyName("timeout_ms")]
    public int? TimeoutMs { get; init; }

    [JsonPropertyName("timeout")]
    public int? TimeoutAlias { get; init; }

    [JsonPropertyName("login")]
    public bool? Login { get; init; }

    [JsonPropertyName("sandbox_permissions")]
    public string? SandboxPermissions { get; init; }

    [JsonPropertyName("prefix_rule")]
    public string[]? PrefixRule { get; init; }

    [JsonPropertyName("additional_permissions")]
    public KernelAdditionalPermissionArguments? AdditionalPermissions { get; init; }

    [JsonPropertyName("justification")]
    public string? Justification { get; init; }

    public int? ResolveTimeoutMs()
        => TimeoutMs ?? TimeoutAlias;
}

/// <summary>
/// request_permissions 的严格参数模型。
/// Strict argument contract for request_permissions.
/// </summary>
internal sealed class KernelRequestPermissionsToolCallArguments
{
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonPropertyName("permissions")]
    public KernelRequestPermissionArguments? Permissions { get; init; }
}

internal sealed class KernelRequestPermissionArguments
{
    [JsonPropertyName("network")]
    public KernelNetworkPermissionArguments? Network { get; init; }

    [JsonPropertyName("file_system")]
    public KernelFileSystemPermissionArguments? FileSystem { get; init; }
}

internal sealed class KernelAdditionalPermissionArguments
{
    [JsonPropertyName("network")]
    public KernelNetworkPermissionArguments? Network { get; init; }

    [JsonPropertyName("file_system")]
    public KernelFileSystemPermissionArguments? FileSystem { get; init; }

    [JsonPropertyName("macos")]
    public KernelMacOsPermissionArguments? MacOs { get; init; }
}

internal sealed class KernelNetworkPermissionArguments
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }
}

internal sealed class KernelFileSystemPermissionArguments
{
    [JsonPropertyName("read")]
    public string[]? Read { get; init; }

    [JsonPropertyName("write")]
    public string[]? Write { get; init; }
}

internal sealed class KernelMacOsPermissionArguments
{
    [JsonPropertyName("preferences")]
    public string? Preferences { get; init; }

    [JsonPropertyName("automations")]
    public string[]? Automations { get; init; }

    [JsonPropertyName("accessibility")]
    public bool? Accessibility { get; init; }

    [JsonPropertyName("calendar")]
    public bool? Calendar { get; init; }

    [JsonPropertyName("launch_services")]
    public bool? LaunchServices { get; init; }

    [JsonPropertyName("reminders")]
    public bool? Reminders { get; init; }

    [JsonPropertyName("contacts")]
    public string? Contacts { get; init; }
}

/// <summary>
/// 对工具参数执行严格 JSON 反序列化，禁止未声明字段静默流入。
/// Performs strict JSON deserialization for tool arguments and rejects undeclared fields.
/// </summary>
internal static class KernelToolArgumentParser
{
    private static readonly JsonSerializerOptions StrictOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static bool TryParse<TArguments>(
        JsonElement arguments,
        out TArguments? value,
        out string? errorMessage)
        where TArguments : class
    {
        value = null;
        errorMessage = null;

        if (arguments.ValueKind != JsonValueKind.Object)
        {
            errorMessage = "tool arguments must be a JSON object";
            return false;
        }

        try
        {
            value = arguments.Deserialize<TArguments>(StrictOptions);
            if (value is null)
            {
                errorMessage = "tool arguments payload is empty";
                return false;
            }

            return true;
        }
        catch (JsonException ex)
        {
            errorMessage = $"tool arguments payload is invalid: {ex.Message}";
            return false;
        }
    }
}

internal sealed class KernelStringArrayOrStringifiedJsonArrayConverter : JsonConverter<string[]?>
{
    public override string[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var values = new List<string>();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    return values.ToArray();
                }

                if (reader.TokenType != JsonTokenType.String)
                {
                    throw new JsonException("command 数组元素必须是字符串。");
                }

                values.Add(reader.GetString() ?? string.Empty);
            }

            throw new JsonException("command 数组不完整。");
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return [];
            }

            var trimmed = text.Trim();
            if (!trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                throw new JsonException("command 字符串必须是 JSON 字符串数组。");
            }

            try
            {
                return JsonSerializer.Deserialize<string[]>(trimmed, options) ?? [];
            }
            catch (JsonException ex)
            {
                throw new JsonException("command 字符串必须是 JSON 字符串数组。", ex);
            }
        }

        throw new JsonException("command 必须是 JSON 数组或字符串化后的 JSON 数组。");
    }

    public override void Write(Utf8JsonWriter writer, string[]? value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, options);
}
