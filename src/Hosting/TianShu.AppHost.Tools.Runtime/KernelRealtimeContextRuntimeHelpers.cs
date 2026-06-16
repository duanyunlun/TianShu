using System.Text.Json;
using TianShu.AppHost.Configuration;
using TianShuPromptConfiguration = TianShu.Configuration.TianShuPromptConfiguration;
using TianShuPromptConfigUtilities = TianShu.Configuration.TianShuPromptConfigUtilities;

namespace TianShu.AppHost.Tools.Runtime;

internal static class KernelRealtimeContextRuntimeHelpers
{
    private const string RealtimeStartupContextHeader = "来自 TianShu 的启动上下文。\n这是关于近期工作、机器与工作区布局的背景信息，可能不完整或已过期。请用它辅助判断，不要在无关场景中复述。";
    private const string RealtimeConversationOpenTag = "<realtime_conversation>";
    private const string RealtimeConversationCloseTag = "</realtime_conversation>";
    private static readonly string DefaultRealtimeStartInstructions =
        """
        实时会话已开始。

        你作为中间层背后的后端执行者运行。用户不会直接与你对话；你的任何回复都会先被中间层消费，并可能在用户看到前被摘要。

        被调用时，你会收到最新会话转录以及相关模式或元数据。即使后端帮助并非真正必要，中间层也可能调用你。请根据转录判断是否需要实际工作；如果不需要后端帮助，避免冗长回复增加用户可感知延迟。

        当用户文本来自实时通道时，请把它当作转录文本处理；它可能缺少标点，也可能包含识别错误。

        - 回复应简洁并面向行动。你的更新应帮助中间层回应用户。
        """;
    private static readonly string DefaultRealtimeEndInstructions =
        """
        实时会话已结束。

        后续用户输入将回到键入文本，而不是转录文本。实时会话结束后，不要再默认存在识别错误或标点缺失；恢复正常聊天行为。
        """;
    private static readonly HashSet<string> RealtimeNoisyDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".next",
        ".pytest_cache",
        ".ruff_cache",
        "__pycache__",
        "bin",
        "build",
        "dist",
        "node_modules",
        "obj",
        "out",
        "target",
    };

    public static KernelRealtimeSessionState BuildConfiguredRealtimeSessionState(
        KernelThreadRecord thread,
        string threadId,
        string sessionId,
        string requestPrompt,
        Func<string?, Dictionary<string, object?>> readRuntimeConfig)
    {
        var cwd = Normalize(thread.Cwd) ?? Environment.CurrentDirectory;
        var config = readRuntimeConfig(cwd);
        var promptConfiguration = TianShuPromptConfigUtilities.FromConfig(config);
        var realtimeWebSocketBaseUrl = Normalize(ReadStringExact(
            config,
            "experimental_realtime_ws_base_url"));
        var configuredModel = Normalize(ReadStringExact(
            config,
            "experimental_realtime_ws_model"));
        var sessionMode = ResolveRealtimeSessionMode(config);
        var eventParser = ResolveRealtimeEventParser(config);
        var configuredPrompt = ReadStringExact(
            config,
            "experimental_realtime_ws_backend_prompt");
        var effectivePrompt = configuredPrompt ?? requestPrompt;
        var configuredStartupContext = ReadStringExact(
            config,
            "experimental_realtime_ws_startup_context");
        var effectiveStartupContext = configuredStartupContext ?? BuildRealtimeStartupContext(thread, promptConfiguration);
        var effectiveInstructions = string.IsNullOrEmpty(effectiveStartupContext)
            ? effectivePrompt
            : $"{effectivePrompt}\n\n{effectiveStartupContext}";

        return new KernelRealtimeSessionState(
            threadId,
            sessionId,
            effectiveInstructions,
            configuredModel,
            realtimeWebSocketBaseUrl,
            eventParser,
            sessionMode);
    }

    public static string BuildRealtimeStartDeveloperInstruction(
        string? cwd,
        Func<string?, Dictionary<string, object?>> readRuntimeConfig)
    {
        var config = readRuntimeConfig(cwd);
        var instructions = ReadStringExact(
            config,
            "experimental_realtime_start_instructions");
        var promptConfiguration = TianShuPromptConfigUtilities.FromConfig(config);
        var sections = new List<string>
        {
            instructions
            ?? promptConfiguration.RealtimeStartInstructions
            ?? DefaultRealtimeStartInstructions,
        };
        return
            $"{RealtimeConversationOpenTag}{Environment.NewLine}{string.Join(Environment.NewLine + Environment.NewLine, sections)}{Environment.NewLine}{RealtimeConversationCloseTag}";
    }

    public static string? ResolveRealtimeDeveloperInstructions(
        KernelRuntimeThread runtimeThread,
        string? cwd,
        Func<string?, Dictionary<string, object?>> readRuntimeConfig)
    {
        return runtimeThread.RealtimeSession is not null
            ? BuildRealtimeStartDeveloperInstruction(cwd, readRuntimeConfig)
            : runtimeThread.ConsumePendingRealtimeEndReason() is string reason
                ? BuildRealtimeEndDeveloperInstruction(reason, TianShuPromptConfigUtilities.FromConfig(readRuntimeConfig(cwd)))
                : null;
    }

    public static string BuildRealtimeEndDeveloperInstruction(string reason)
        => BuildRealtimeEndDeveloperInstruction(reason, TianShuPromptConfiguration.Empty);

    private static string BuildRealtimeEndDeveloperInstruction(string reason, TianShuPromptConfiguration promptConfiguration)
    {
        var endInstructions = promptConfiguration.RealtimeEndInstructions ?? DefaultRealtimeEndInstructions;
        return
            $"{RealtimeConversationOpenTag}{Environment.NewLine}{endInstructions}{Environment.NewLine}{Environment.NewLine}原因：{reason}{Environment.NewLine}{RealtimeConversationCloseTag}";
    }

    private static KernelRealtimeSessionMode ResolveRealtimeSessionMode(Dictionary<string, object?> config)
    {
        var rawMode = Normalize(ReadStringExact(config, "experimental_realtime_ws_mode"));
        return string.Equals(rawMode, "transcription", StringComparison.OrdinalIgnoreCase)
            ? KernelRealtimeSessionMode.Transcription
            : KernelRealtimeSessionMode.Conversational;
    }

    private static KernelRealtimeEventParser ResolveRealtimeEventParser(Dictionary<string, object?> config)
    {
        var enabled = ReadBooleanExact(config, ["features", "realtime_conversation_v2"])
            ?? ReadBooleanExact(config, ["features", "responses_websockets_v2"])
            ?? false;
        return enabled
            ? KernelRealtimeEventParser.RealtimeV2
            : KernelRealtimeEventParser.V1;
    }

    private static string? ReadStringExact(Dictionary<string, object?> config, string propertyName)
        => TryReadValueExact(config, propertyName, out var rawValue)
           && TryReadString(rawValue, out var value)
            ? value
            : null;

    private static bool? ReadBooleanExact(Dictionary<string, object?> config, IReadOnlyList<string> propertyPath)
        => TryReadNestedValueExact(config, propertyPath, out var rawValue)
           && TryReadBoolean(rawValue, out var value)
            ? value
            : null;

    private static bool TryReadValueExact(Dictionary<string, object?> config, string propertyName, out object? value)
        => config.TryGetValue(propertyName, out value);

    private static bool TryReadNestedValueExact(
        Dictionary<string, object?> config,
        IReadOnlyList<string> propertyPath,
        out object? value)
    {
        var current = config;
        for (var index = 0; index < propertyPath.Count; index++)
        {
            if (!TryReadValueExact(current, propertyPath[index], out value))
            {
                return false;
            }

            if (index == propertyPath.Count - 1)
            {
                return true;
            }

            if (!TryAsDictionary(value, out current))
            {
                value = null;
                return false;
            }
        }

        value = null;
        return false;
    }

    private static bool TryAsDictionary(object? value, out Dictionary<string, object?> dictionary)
    {
        switch (value)
        {
            case Dictionary<string, object?> concrete:
                dictionary = concrete;
                return true;
            case IReadOnlyDictionary<string, object?> readOnly:
                dictionary = readOnly.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
                return true;
            case IDictionary<string, object?> mutable:
                dictionary = mutable.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.Object:
                dictionary = ConvertJsonObject(element);
                return true;
            default:
                dictionary = null!;
                return false;
        }
    }

    private static bool TryReadString(object? value, out string text)
    {
        switch (value)
        {
            case string stringValue:
                text = stringValue;
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.String:
                text = element.GetString() ?? string.Empty;
                return true;
            default:
                text = string.Empty;
                return false;
        }
    }

    private static bool TryReadBoolean(object? value, out bool booleanValue)
    {
        switch (value)
        {
            case bool native:
                booleanValue = native;
                return true;
            case JsonElement element when element.ValueKind is JsonValueKind.True or JsonValueKind.False:
                booleanValue = element.GetBoolean();
                return true;
            case string text when bool.TryParse(text, out var parsed):
                booleanValue = parsed;
                return true;
            default:
                booleanValue = default;
                return false;
        }
    }

    private static Dictionary<string, object?> ConvertJsonObject(JsonElement element)
    {
        var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            dictionary[property.Name] = ConvertJsonValue(property.Value);
        }

        return dictionary;
    }

    private static object? ConvertJsonValue(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Object => ConvertJsonObject(element),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonValue).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => element.TryGetInt64(out var intValue)
                ? intValue
                : element.TryGetDouble(out var doubleValue)
                    ? doubleValue
                    : element.GetRawText(),
            JsonValueKind.Null => null,
            _ => element.GetRawText(),
        };

    private static string BuildRealtimeStartupContext(
        KernelThreadRecord thread,
        TianShuPromptConfiguration promptConfiguration)
    {
        var recentWorkSection = BuildRealtimeRecentWorkSection(thread);
        var workspaceSection = BuildRealtimeWorkspaceSection(Normalize(thread.Cwd) ?? Environment.CurrentDirectory);
        if (string.IsNullOrWhiteSpace(recentWorkSection) && string.IsNullOrWhiteSpace(workspaceSection))
        {
            return string.Empty;
        }

        var sections = new List<string>
        {
            promptConfiguration.RealtimeStartupContextHeader ?? RealtimeStartupContextHeader,
        };
        if (!string.IsNullOrWhiteSpace(recentWorkSection))
        {
            sections.Add(recentWorkSection);
        }

        if (!string.IsNullOrWhiteSpace(workspaceSection))
        {
            sections.Add(workspaceSection);
        }

        return string.Join(Environment.NewLine + Environment.NewLine, sections);
    }

    private static string? BuildRealtimeRecentWorkSection(KernelThreadRecord thread)
    {
        var lastUserMessage = Normalize(thread.LastUserMessage)
            ?? thread.Turns.LastOrDefault(static turn => !string.IsNullOrWhiteSpace(turn.UserMessage))?.UserMessage;
        var lastAssistantMessage = Normalize(thread.LastAssistantMessage)
            ?? thread.Turns.LastOrDefault(static turn => !string.IsNullOrWhiteSpace(turn.AssistantMessage))?.AssistantMessage;
        var branch = Normalize(thread.GitInfo?.Branch);
        if (string.IsNullOrWhiteSpace(lastUserMessage)
            && string.IsNullOrWhiteSpace(lastAssistantMessage)
            && string.IsNullOrWhiteSpace(branch))
        {
            return null;
        }

        var lines = new List<string>
        {
            "## 近期工作",
            $"近期会话数：{Math.Max(1, thread.Turns.Count)}",
        };
        if (!string.IsNullOrWhiteSpace(branch))
        {
            lines.Add($"最新分支：{branch}");
        }

        if (!string.IsNullOrWhiteSpace(lastUserMessage))
        {
            lines.Add($"用户请求：{TrimRealtimeContextLine(lastUserMessage!)}");
        }

        if (!string.IsNullOrWhiteSpace(lastAssistantMessage))
        {
            lines.Add($"上一条助手回复：{TrimRealtimeContextLine(lastAssistantMessage!)}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string? BuildRealtimeWorkspaceSection(string cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd) || !Directory.Exists(cwd))
        {
            return null;
        }

        var lines = new List<string>
        {
            "## 机器 / 工作区地图",
            $"当前工作目录：{cwd}",
        };
        AppendRealtimeWorkspaceEntries(lines, cwd, depth: 0, maxDepth: 2, maxEntries: 20);
        return string.Join(Environment.NewLine, lines);
    }

    private static void AppendRealtimeWorkspaceEntries(
        List<string> lines,
        string directory,
        int depth,
        int maxDepth,
        int maxEntries)
    {
        if (depth >= maxDepth)
        {
            return;
        }

        IEnumerable<string> entries;
        try
        {
            entries = Directory
                .EnumerateFileSystemEntries(directory)
                .OrderBy(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .Where(static path => !RealtimeNoisyDirectoryNames.Contains(Path.GetFileName(path)))
                .Take(maxEntries)
                .ToArray();
        }
        catch
        {
            return;
        }

        foreach (var entry in entries)
        {
            var name = Path.GetFileName(entry);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var indent = new string(' ', depth * 2);
            if (Directory.Exists(entry))
            {
                lines.Add($"{indent}- {name}/");
                AppendRealtimeWorkspaceEntries(lines, entry, depth + 1, maxDepth, maxEntries);
            }
            else
            {
                lines.Add($"{indent}- {name}");
            }
        }
    }

    private static string TrimRealtimeContextLine(string text)
    {
        const int maxLength = 240;
        if (text.Length <= maxLength)
        {
            return text;
        }

        return text[..(maxLength - 1)] + "…";
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
