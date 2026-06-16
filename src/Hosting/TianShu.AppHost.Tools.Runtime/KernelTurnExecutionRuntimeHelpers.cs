using System.Text;
using System.Text.Json;
using TianShu.AppHost.Configuration;
using TianShu.AppHost.State;
using TianShu.AppHost.Tools;
using TianShu.Execution.Runtime;
using TianShu.Provider.Abstractions;
using TianShuPromptConfiguration = TianShu.Configuration.TianShuPromptConfiguration;
using TianShuPromptConfigUtilities = TianShu.Configuration.TianShuPromptConfigUtilities;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// turn execution 期的依赖解析与 prompt 组装辅助件。
/// Helpers for turn-execution dependency resolution and prompt composition.
/// </summary>
internal static class KernelTurnExecutionRuntimeHelpers
{
    internal const int PromptConversationTurnWindow = 12;
    internal const int PromptSeedHistoryWindow = 24;

    internal const string UserFacingLanguagePolicyInstruction =
        "语言策略：面向用户的说明、update_plan 步骤、审批提示、输入请求和状态文本，都应跟随用户当前使用的语言。用户使用中文时，这些面向用户的文本必须保持中文。";

    internal const string FreeformApplyPatchDeveloperInstruction =
        """
        ## apply_patch

        使用 `apply_patch` 工具编辑文件。这是 FREEFORM 工具，调用时不要把补丁包装成 JSON。
        补丁语言是一种精简的、面向文件的 diff 格式，目标是易于解析并能安全应用。可以把它理解为一个高层补丁信封：

        *** Begin Patch
        [ 一个或多个文件段 ]
        *** End Patch

        在这个信封中，可以包含一系列文件操作。
        必须包含一个头部来说明你要执行的动作。
        每个操作都以以下三种头部之一开始：

        *** Add File: <path> - 创建新文件。后续每一行都必须是以 + 开头的初始内容。
        *** Delete File: <path> - 删除已有文件。该头部后不跟任何内容。
        *** Update File: <path> - 就地修改已有文件，也可以同时重命名。

        示例补丁：

        *** Begin Patch
        *** Add File: hello.txt
        +Hello world
        *** Update File: src/app.py
        *** Move to: src/main.py
        @@ def greet():
        -print("Hi")
        +print("Hello, world!")
        *** Delete File: obsolete.txt
        *** End Patch

        需要记住：
        - 必须包含说明动作的头部（Add/Delete/Update）。
        - 新增行必须以 `+` 开头，即使是在创建新文件。
        - 补丁头部中的路径必须使用仓库相对路径；绝对路径会被拒绝。
        """;

    public static async Task<TurnRequestContext> ResolveTurnDependenciesAsync(
        TurnOperationState state,
        TurnRequestContext context,
        Func<IReadOnlyList<KernelTurnInputItem>?, string, CancellationToken, Task<string?>> buildExplicitPluginInstructionsAsync,
        Func<TurnRequestContext, string, CancellationToken, Task<List<KernelSkillDescriptor>>> resolveMentionedSkillsAsync,
        Func<IReadOnlyList<KernelSkillDescriptor>, List<string>> buildSkillInjectionMessages,
        Func<TurnOperationState, TurnRequestContext, IReadOnlyList<KernelSkillDescriptor>, CancellationToken, Task> resolveSkillEnvironmentDependenciesAsync,
        Func<TurnOperationState, TurnRequestContext, IReadOnlyList<KernelSkillDescriptor>, CancellationToken, Task> resolveSkillMcpDependenciesAsync,
        CancellationToken cancellationToken)
    {
        var effectiveContext = context with
        {
            ExplicitPluginInstructions = await buildExplicitPluginInstructionsAsync(
                context.InputItems,
                state.EffectiveUserText,
                cancellationToken).ConfigureAwait(false),
        };

        var skills = await resolveMentionedSkillsAsync(effectiveContext, state.EffectiveUserText, cancellationToken).ConfigureAwait(false);
        var skillInjections = effectiveContext.ExplicitSkillInjections
            ?? throw new InvalidOperationException("turn context missing skill injection collection");
        skillInjections.Clear();
        skillInjections.AddRange(buildSkillInjectionMessages(skills));

        if (skills.Count == 0)
        {
            return effectiveContext;
        }

        await resolveSkillEnvironmentDependenciesAsync(state, effectiveContext, skills, cancellationToken).ConfigureAwait(false);
        await resolveSkillMcpDependenciesAsync(state, effectiveContext, skills, cancellationToken).ConfigureAwait(false);
        return effectiveContext;
    }

    public static TurnRequestContext RefreshLoopTurnContext(
        string threadId,
        TurnRequestContext current,
        KernelThreadManager threadManager,
        Func<string?, string> buildRealtimeStartDeveloperInstruction)
    {
        if (!threadManager.TryGetThread(threadId, out var runtimeThread) || runtimeThread is null)
        {
            return current;
        }

        return current with
        {
            DynamicTools = KernelDynamicToolResolver.Clone(runtimeThread.Session.DynamicTools),
            RealtimeDeveloperInstructions = runtimeThread.RealtimeSession is not null
                ? buildRealtimeStartDeveloperInstruction(runtimeThread.Session.Cwd)
                : current.RealtimeDeveloperInstructions,
        };
    }

    public static string ResolveTurnInstructions(TurnRequestContext context)
        => Normalize(context.BaseInstructions) ?? string.Empty;

    public static IReadOnlyList<string>? ResolveContextualUserMessages(TurnRequestContext context)
    {
        List<string>? messages = null;

        var serializedUserInstructions = KernelInstructionScopeUtilities.SerializeUserInstructions(context.Cwd, context.UserInstructions);
        if (!string.IsNullOrWhiteSpace(serializedUserInstructions))
        {
            messages = [serializedUserInstructions!];
        }

        if (context.ExplicitSkillInjections is not null)
        {
            foreach (var injection in context.ExplicitSkillInjections)
            {
                var normalized = Normalize(injection);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    continue;
                }

                messages ??= [];
                messages.Add(normalized!);
            }
        }

        var serializedEnvironmentContextSubagents = SerializeEnvironmentContextSubagents(context.EnvironmentContextSubagents);
        if (!string.IsNullOrWhiteSpace(serializedEnvironmentContextSubagents))
        {
            messages ??= [];
            messages.Add(serializedEnvironmentContextSubagents!);
        }

        return messages;
    }

    public static string? ResolveTurnDeveloperMessage(TurnRequestContext context, bool includeBaseInstructions)
    {
        var sections = new List<string>();
        if (includeBaseInstructions)
        {
            AppendInstructionSection(sections, context.BaseInstructions);
        }

        var promptConfiguration = context.PromptConfiguration ?? TianShuPromptConfiguration.Empty;
        if (ProviderModelCatalogs.UsesFreeformApplyPatchTool(context.Model))
        {
            AppendInstructionSection(
                sections,
                TianShuPromptConfigUtilities.ApplySection(promptConfiguration.ApplyPatch, FreeformApplyPatchDeveloperInstruction));
        }

        AppendInstructionSection(sections, context.DeveloperInstructions);
        AppendInstructionSection(sections, ResolveCollaborationModeInstructions(
            context.CollaborationMode,
            promptConfiguration,
            context.DefaultModeRequestUserInputEnabled));
        AppendInstructionSection(sections, context.ExplicitPluginInstructions);
        AppendInstructionSection(sections, context.RealtimeDeveloperInstructions);
        AppendInstructionSection(
            sections,
            TianShuPromptConfigUtilities.ApplySection(promptConfiguration.LanguagePolicy, UserFacingLanguagePolicyInstruction));
        return sections.Count == 0
            ? null
            : string.Join(Environment.NewLine + Environment.NewLine, sections);
    }

    public static List<object> BuildResponsesConversationInput(
        KernelThreadRecord? thread,
        string userText,
        string? developerInstructions,
        IReadOnlyList<string>? contextualUserMessages,
        IReadOnlyList<KernelTurnInputItem>? currentInputItems,
        bool includeProviderReplayArtifacts = false)
        => BuildSlicedResponsesConversationInput(
            thread,
            userText,
            developerInstructions,
            contextualUserMessages,
            currentInputItems,
            includeProviderReplayArtifacts).Input;

    public static List<Dictionary<string, object?>> BuildProviderMessages(
        KernelThreadRecord? thread,
        string userText,
        string? developerInstructions,
        IReadOnlyList<string>? contextualUserMessages,
        IReadOnlyList<KernelTurnInputItem>? currentInputItems,
        KernelPromptContentFormat contentFormat,
        ContextBudgetProfile? budgetProfile = null)
    {
        var messages = new List<Dictionary<string, object?>>();
        if (!string.IsNullOrWhiteSpace(Normalize(developerInstructions)))
        {
            messages.Add(new Dictionary<string, object?>
            {
                ["role"] = "developer",
                ["content"] = developerInstructions,
            });
        }

        if (contextualUserMessages is not null)
        {
            foreach (var contextualUserMessage in contextualUserMessages)
            {
                if (string.IsNullOrWhiteSpace(Normalize(contextualUserMessage)))
                {
                    continue;
                }

                messages.Add(new Dictionary<string, object?>
                {
                    ["role"] = "user",
                    ["content"] = contextualUserMessage,
                });
            }
        }

        if (thread is not null)
        {
            foreach (var historyItem in thread.SeedHistory)
            {
                AppendProviderConversationHistoryItem(messages, historyItem, contentFormat);
            }

            foreach (var turn in SelectTurnsForPromptWindow(
                         thread.Turns,
                         int.MaxValue,
                         Normalize(userText) ?? userText,
                         currentInputItems))
            {
                foreach (var historyItem in EnumerateTurnConversationHistoryItems(turn))
                {
                    AppendProviderConversationHistoryItem(messages, historyItem, contentFormat);
                }
            }
        }

        var currentInputs = KernelConversationHistoryUtilities.ParseInputItems(currentInputItems);
        var currentStructuredContent = KernelConversationHistoryUtilities.BuildProviderContentItems(
            currentInputs,
            contentFormat);
        if (contentFormat != KernelPromptContentFormat.PlainText && currentStructuredContent.Length > 0)
        {
            messages.Add(new Dictionary<string, object?>
            {
                ["role"] = "user",
                ["content"] = currentStructuredContent,
            });
        }
        else
        {
            messages.Add(new Dictionary<string, object?>
            {
                ["role"] = "user",
                ["content"] = Normalize(userText) ?? userText,
            });
        }

        return ContextSlicingRuntimeHelpers.SliceProviderMessages(
            messages,
            thread?.Id,
            budgetProfile: budgetProfile);
    }

    public static ContextSlicedResponsesConversationInput BuildSlicedResponsesConversationInput(
        KernelThreadRecord? thread,
        string userText,
        string? developerInstructions,
        IReadOnlyList<string>? contextualUserMessages,
        IReadOnlyList<KernelTurnInputItem>? currentInputItems,
        bool includeProviderReplayArtifacts = false,
        ContextBudgetProfile? budgetProfile = null,
        int? providerEffectiveContextLimitTokens = null,
        IReadOnlyList<ContextSegment>? overlaySegments = null,
        string? turnId = null,
        string? modelId = null,
        string? providerId = null)
    {
        var originalInput = BuildResponsesConversationInputLegacy(
            thread,
            userText,
            developerInstructions,
            contextualUserMessages,
            currentInputItems,
            includeProviderReplayArtifacts);
        var candidateSegments = BuildResponsesConversationInputSegments(originalInput, overlaySegments);
        var planner = new ContextSlicePlanner(new ApproximateContextTokenEstimator());
        var result = planner.Plan(new ContextSliceRequest
        {
            ThreadId = thread?.Id ?? "current-thread",
            TurnId = turnId ?? "current-turn",
            BudgetProfile = budgetProfile ?? ContextBudgetProfile.Default,
            ProviderEffectiveContextLimitTokens = providerEffectiveContextLimitTokens,
            CandidateSegments = candidateSegments,
        });

        var includedSegments = EnsureToolReplayPairSegments(result.IncludedSegments, candidateSegments);
        var report = RebuildReportIfIncludedSegmentsChanged(result, includedSegments);

        return new ContextSlicedResponsesConversationInput(
            MaterializeResponsesConversationInput(BuildMaterializedContextSegments(includedSegments, result)),
            report with
            {
                ModelId = modelId,
                ProviderId = providerId,
            });
    }

    public static ContextSlicedResponsesConversationInput BuildSlicedResponsesFollowUpInput(
        IReadOnlyList<object> priorInput,
        IReadOnlyList<object> responseItems,
        IReadOnlyList<object> nextInput,
        string threadId,
        string turnId,
        ContextBudgetProfile? budgetProfile = null,
        int? providerEffectiveContextLimitTokens = null,
        string? modelId = null,
        string? providerId = null)
    {
        var input = KernelAutoCompactionRuntimeHelpers.BuildResponsesFollowUpInput(priorInput, responseItems, nextInput);
        var candidateSegments = BuildResponsesConversationInputSegments(input);
        var planner = new ContextSlicePlanner(new ApproximateContextTokenEstimator());
        var result = planner.Plan(new ContextSliceRequest
        {
            ThreadId = threadId,
            TurnId = turnId,
            BudgetProfile = budgetProfile ?? ContextBudgetProfile.Default,
            ProviderEffectiveContextLimitTokens = providerEffectiveContextLimitTokens,
            CandidateSegments = candidateSegments,
        });

        var includedSegments = EnsureToolReplayPairSegments(result.IncludedSegments, candidateSegments);
        var report = RebuildReportIfIncludedSegmentsChanged(result, includedSegments);

        return new ContextSlicedResponsesConversationInput(
            MaterializeResponsesConversationInput(BuildMaterializedContextSegments(includedSegments, result)),
            report with
            {
                ModelId = modelId,
                ProviderId = providerId,
            });
    }

    private static IReadOnlyList<ContextSegment> EnsureToolReplayPairSegments(
        IReadOnlyList<ContextSegment> includedSegments,
        IReadOnlyList<ContextSegment> candidateSegments)
    {
        var includedIds = new HashSet<string>(
            includedSegments.Select(static segment => segment.Id),
            StringComparer.Ordinal);
        var requiredCallIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var segment in includedSegments)
        {
            if (TryReadToolReplayShape(segment.StructuredContent, out var itemType, out var callId)
                && string.Equals(itemType, "function_call_output", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(callId))
            {
                requiredCallIds.Add(callId!);
            }
        }

        if (requiredCallIds.Count == 0)
        {
            return includedSegments;
        }

        var restored = new List<ContextSegment>(includedSegments);
        foreach (var candidate in candidateSegments)
        {
            if (includedIds.Contains(candidate.Id)
                || !TryReadToolReplayShape(candidate.StructuredContent, out var itemType, out var callId)
                || !string.Equals(itemType, "function_call", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(callId)
                || !requiredCallIds.Contains(callId!))
            {
                continue;
            }

            restored.Add(candidate);
            includedIds.Add(candidate.Id);
        }

        return restored.Count == includedSegments.Count
            ? includedSegments
            : restored
                .OrderBy(static segment => segment.Metadata.TryGetValue("responsesInputOrder", out var rawOrder)
                    ? rawOrder switch
                    {
                        int intOrder => intOrder,
                        long longOrder => longOrder,
                        double doubleOrder => doubleOrder,
                        _ => int.MaxValue,
                    }
                    : int.MaxValue)
                .ToArray();
    }

    private static ContextSlicingReport RebuildReportIfIncludedSegmentsChanged(
        ContextSliceResult result,
        IReadOnlyList<ContextSegment> includedSegments)
    {
        if (includedSegments.Count == result.IncludedSegments.Count)
        {
            return result.Report;
        }

        var includedIds = new HashSet<string>(
            includedSegments.Select(static segment => segment.Id),
            StringComparer.Ordinal);

        return result.Report with
        {
            EstimatedIncludedTokens = includedSegments.Sum(static segment => segment.EstimatedTokens),
            IncludedSegments = includedSegments.Select(static segment => ToReportEntry(segment, null)).ToArray(),
            DroppedSegments = result.DroppedSegments
                .Where(item => !includedIds.Contains(item.Segment.Id))
                .Select(static item => ToReportEntry(item.Segment, item.Reason))
                .ToArray(),
        };
    }

    private static ContextSegmentReportEntry ToReportEntry(ContextSegment segment, DroppedContextReason? reason)
        => new(
            segment.Id,
            segment.Kind,
            segment.Priority,
            segment.EstimatedTokens,
            reason,
            segment.SourceRefs);

    private static IReadOnlyList<ContextSegment> BuildMaterializedContextSegments(
        IReadOnlyList<ContextSegment> includedSegments,
        ContextSliceResult result)
    {
        if (result.SummarizedSegments.Count == 0 && result.ReferenceOnlySegments.Count == 0)
        {
            return includedSegments;
        }

        var materialized = new List<ContextSegment>(includedSegments.Count + 1);
        materialized.AddRange(includedSegments);
        materialized.Add(CreateOverflowMaterializationSegment(includedSegments, result));
        return materialized;
    }

    private static ContextSegment CreateOverflowMaterializationSegment(
        IReadOnlyList<ContextSegment> includedSegments,
        ContextSliceResult result)
    {
        var sections = new List<string>();
        if (result.SummarizedSegments.Count > 0)
        {
            sections.Add("Summarized overflow segments:");
            sections.AddRange(result.SummarizedSegments.Select(static segment => "- " + FormatOverflowSegment(segment)));
        }

        if (result.ReferenceOnlySegments.Count > 0)
        {
            sections.Add("Reference-only overflow segments:");
            sections.AddRange(result.ReferenceOnlySegments.Select(static segment => "- " + FormatOverflowSegment(segment)));
        }

        var order = ResolveOverflowMaterializationOrder(includedSegments);
        return new ContextSegment
        {
            Id = "context-overflow-materialization",
            Kind = ContextSegmentKind.HistoricalSummary,
            Priority = ContextSegmentPriority.High,
            RetentionPolicy = ContextRetentionPolicy.MustKeep,
            Text = string.Join(Environment.NewLine, sections),
            EstimatedTokens = Math.Max(16, sections.Sum(static section => section.Length) / 4),
            Confidence = 1.0d,
            AuthorityWeight = 50,
            RecencyWeight = 50,
            RelevanceScore = 1.0d,
            SourceRefs = result.SummarizedSegments
                .Concat(result.ReferenceOnlySegments)
                .SelectMany(static segment => segment.SourceRefs)
                .ToArray(),
            Metadata = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["responsesInputOrder"] = order,
                ["contextOverflowMaterialization"] = true,
            },
        };
    }

    private static double ResolveOverflowMaterializationOrder(IReadOnlyList<ContextSegment> includedSegments)
    {
        var maxOrder = includedSegments
            .Select(static segment => TryReadResponsesInputOrder(segment, out var order) ? order : (double?)null)
            .Where(static order => order.HasValue)
            .Select(static order => order!.Value)
            .DefaultIfEmpty(double.MaxValue)
            .Max();
        return double.IsFinite(maxOrder) ? maxOrder - 0.25d : double.MaxValue - 1;
    }

    private static bool TryReadResponsesInputOrder(ContextSegment segment, out double order)
    {
        order = 0;
        if (!segment.Metadata.TryGetValue("responsesInputOrder", out var rawOrder))
        {
            return false;
        }

        switch (rawOrder)
        {
            case int intOrder:
                order = intOrder;
                return true;
            case long longOrder:
                order = longOrder;
                return true;
            case double doubleOrder:
                order = doubleOrder;
                return true;
            default:
                return false;
        }
    }

    private static string FormatOverflowSegment(ContextSegment segment)
    {
        var refs = segment.SourceRefs.Count == 0
            ? "source=untracked"
            : "source=" + string.Join(",", segment.SourceRefs.Select(FormatSourceRef));
        return $"id={segment.Id}; kind={segment.Kind}; {refs}";
    }

    private static string FormatSourceRef(ContextSourceRef source)
    {
        var parts = new List<string> { source.Kind.ToString() };
        if (!string.IsNullOrWhiteSpace(source.Id))
        {
            parts.Add(source.Id!);
        }

        if (!string.IsNullOrWhiteSpace(source.ThreadId))
        {
            parts.Add($"thread={source.ThreadId}");
        }

        if (!string.IsNullOrWhiteSpace(source.TurnId))
        {
            parts.Add($"turn={source.TurnId}");
        }

        if (!string.IsNullOrWhiteSpace(source.Path))
        {
            parts.Add($"path={source.Path}");
        }

        return string.Join(":", parts);
    }

    private static List<object> BuildResponsesConversationInputLegacy(
        KernelThreadRecord? thread,
        string userText,
        string? developerInstructions,
        IReadOnlyList<string>? contextualUserMessages,
        IReadOnlyList<KernelTurnInputItem>? currentInputItems,
        bool includeProviderReplayArtifacts = false)
    {
        var list = new List<object>();
        if (!string.IsNullOrWhiteSpace(Normalize(developerInstructions)))
        {
            list.Add(CreateResponsesMessage("developer", "input_text", developerInstructions!));
        }

        if (contextualUserMessages is not null)
        {
            foreach (var contextualUserMessage in contextualUserMessages)
            {
                if (!string.IsNullOrWhiteSpace(Normalize(contextualUserMessage)))
                {
                    list.Add(CreateResponsesMessage("user", "input_text", contextualUserMessage));
                }
            }
        }

        if (thread is not null)
        {
            var (seedHistory, tailContextHistory) = SplitTailContextHistory(thread.SeedHistory);
            foreach (var historyItem in seedHistory)
            {
                AppendResponsesConversationHistoryItem(list, historyItem, includeProviderReplayArtifacts);
            }

            foreach (var turn in SelectTurnsForPromptWindow(
                         thread.Turns,
                         int.MaxValue,
                         Normalize(userText) ?? userText,
                         currentInputItems))
            {
                foreach (var historyItem in EnumerateTurnConversationHistoryItems(turn))
                {
                    AppendResponsesConversationHistoryItem(list, historyItem, includeProviderReplayArtifacts);
                }
            }

            foreach (var historyItem in tailContextHistory)
            {
                AppendResponsesConversationHistoryItem(list, historyItem, includeProviderReplayArtifacts);
            }
        }

        var currentInputs = KernelConversationHistoryUtilities.ParseInputItems(currentInputItems);
        var structuredCurrentContent = KernelConversationHistoryUtilities.BuildProviderContentItems(
            currentInputs,
            KernelPromptContentFormat.Responses);
        if (structuredCurrentContent.Length > 0)
        {
            list.Add(CreateResponsesMessage("user", structuredCurrentContent));
        }
        else
        {
            list.Add(CreateResponsesMessage("user", "input_text", userText));
        }

        return FilterUnmatchedProviderToolCalls(list);
    }

    private static IReadOnlyList<ContextSegment> BuildResponsesConversationInputSegments(
        IReadOnlyList<object> input,
        IReadOnlyList<ContextSegment>? overlaySegments = null)
    {
        var segments = new List<ContextSegment>(input.Count + (overlaySegments?.Count ?? 0));
        var currentInputOrder = Math.Max(0, input.Count - 1);
        for (var index = 0; index < input.Count; index++)
        {
            var item = input[index];
            var (kind, priority, retentionPolicy, sourceKind) = ResolveResponsesInputSegmentShape(item, index, input.Count);
            var (structuredContent, text, estimatedTokens) = BuildResponsesSegmentPayload(item, kind);
            segments.Add(new ContextSegment
            {
                Id = $"responses-input-{index}",
                Kind = kind,
                Priority = priority,
                RetentionPolicy = retentionPolicy,
                Text = text,
                StructuredContent = structuredContent,
                EstimatedTokens = estimatedTokens,
                Confidence = 1.0d,
                AuthorityWeight = priority == ContextSegmentPriority.Critical ? 100 : 30,
                RecencyWeight = index == input.Count - 1 ? 100 : index,
                RelevanceScore = index == input.Count - 1 ? 1.0d : 0.75d,
                SourceRefs =
                [
                    new ContextSourceRef(sourceKind, Id: $"responses-input-{index}"),
                ],
                Metadata = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["responsesInputOrder"] = (double)index,
                },
            });
        }

        if (overlaySegments is { Count: > 0 })
        {
            for (var index = 0; index < overlaySegments.Count; index++)
            {
                var overlay = overlaySegments[index];
                var metadata = new Dictionary<string, object?>(overlay.Metadata, StringComparer.Ordinal)
                {
                    ["responsesInputOrder"] = currentInputOrder - 0.5d + (index * 0.001d),
                };
                segments.Add(overlay with { Metadata = metadata });
            }
        }

        return segments;
    }

    private static (object StructuredContent, string? Text, int EstimatedTokens) BuildResponsesSegmentPayload(
        object item,
        ContextSegmentKind kind)
    {
        if (TryBuildToolArtifactSegmentPayload(item, out var slicedItem, out var text, out var estimatedTokens))
        {
            return (slicedItem, text, estimatedTokens);
        }

        return (item, null, Math.Max(1, JsonSerializer.Serialize(item).Length / 3));
    }

    private static bool TryBuildToolArtifactSegmentPayload(
        object item,
        out object slicedItem,
        out string? text,
        out int estimatedTokens)
    {
        slicedItem = item;
        text = null;
        estimatedTokens = 0;

        if (!TryReadToolReplayShape(item, out var type, out var callId)
            || (!string.Equals(type, "function_call_output", StringComparison.Ordinal)
                && !string.Equals(type, "custom_tool_call_output", StringComparison.Ordinal)))
        {
            return false;
        }

        string? outputText;
        if (!TryReadDictionaryValue(item, "output", out var rawOutput))
        {
            outputText = JsonSerializer.Serialize(item);
        }
        else if (!TryReadStringValue(rawOutput, out outputText))
        {
            return false;
        }

        var artifactSegment = ContextSegmentFactories.CreateToolArtifactSlice(new ContextToolArtifactSliceRequest
        {
            SegmentId = string.IsNullOrWhiteSpace(callId) ? "tool-output" : $"tool-output-{callId}",
            ToolName = type,
            Stdout = outputText,
            ArtifactRef = callId,
            SourceRefs =
            [
                new ContextSourceRef(ContextSourceKind.ToolOutput, Id: callId),
            ],
        });

        text = artifactSegment.Text;
        estimatedTokens = artifactSegment.EstimatedTokens;
        slicedItem = ReplaceDictionaryValue(item, "output", text ?? outputText);
        return true;
    }

    private static (
        ContextSegmentKind Kind,
        ContextSegmentPriority Priority,
        ContextRetentionPolicy RetentionPolicy,
        ContextSourceKind SourceKind) ResolveResponsesInputSegmentShape(
            object item,
            int index,
            int count)
    {
        if (TryReadToolReplayShape(item, out var itemType, out _)
            && (string.Equals(itemType, "function_call_output", StringComparison.Ordinal)
                || string.Equals(itemType, "custom_tool_call_output", StringComparison.Ordinal)))
        {
            return (
                ContextSegmentKind.ToolResult,
                ContextSegmentPriority.High,
                ContextRetentionPolicy.KeepIfRelevant,
                ContextSourceKind.ToolOutput);
        }

        var role = TryReadResponsesMessageRole(item);
        if (string.Equals(role, "developer", StringComparison.OrdinalIgnoreCase))
        {
            return (
                ContextSegmentKind.DeveloperInstruction,
                ContextSegmentPriority.Critical,
                ContextRetentionPolicy.MustKeep,
                ContextSourceKind.Instruction);
        }

        if (index == count - 1 && string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
        {
            return (
                ContextSegmentKind.CurrentUserInput,
                ContextSegmentPriority.Critical,
                ContextRetentionPolicy.MustKeep,
                ContextSourceKind.UserInput);
        }

        return (
            ContextSegmentKind.RecentTurn,
            ContextSegmentPriority.High,
            ContextRetentionPolicy.SummarizeIfDropped,
            ContextSourceKind.ConversationHistory);
    }

    private static string? TryReadResponsesMessageRole(object item)
    {
        if (item is IReadOnlyDictionary<string, object?> readOnlyDictionary
            && readOnlyDictionary.TryGetValue("role", out var rawReadOnlyRole))
        {
            return rawReadOnlyRole as string;
        }

        if (item is IDictionary<string, object?> dictionary
            && dictionary.TryGetValue("role", out var rawRole))
        {
            return rawRole as string;
        }

        return null;
    }

    private static bool TryReadToolReplayShape(object? item, out string? itemType, out string? callId)
    {
        itemType = null;
        callId = null;
        if (item is null)
        {
            return false;
        }

        if (TryReadDictionaryString(item, "type", out itemType))
        {
            _ = TryReadDictionaryString(item, "call_id", out callId);
            return true;
        }

        if (item is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            itemType = ReadJsonElementString(element, "type");
            callId = ReadJsonElementString(element, "call_id");
            return !string.IsNullOrWhiteSpace(itemType);
        }

        return false;
    }

    /// <summary>
    /// 过滤无法安全回放的 provider tool call，避免中断 turn 遗留的未闭合 call 污染下一次 Responses 请求。
    /// Filters provider tool calls that cannot be replayed safely so interrupted turns do not poison the next Responses request.
    /// </summary>
    private static List<object> FilterUnmatchedProviderToolCalls(IReadOnlyList<object> input)
    {
        var outputCallIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in input)
        {
            if (TryReadToolReplayShape(item, out var itemType, out var callId)
                && IsProviderToolCallOutput(itemType)
                && !string.IsNullOrWhiteSpace(callId))
            {
                outputCallIds.Add(callId!);
            }
        }

        if (outputCallIds.Count == 0)
        {
            return input
                .Where(static item => !IsUnmatchedProviderToolCall(item, Array.Empty<string>()))
                .ToList();
        }

        return input
            .Where(item => !IsUnmatchedProviderToolCall(item, outputCallIds))
            .ToList();
    }

    private static bool IsUnmatchedProviderToolCall(object item, IReadOnlyCollection<string> outputCallIds)
        => TryReadToolReplayShape(item, out var itemType, out var callId)
           && IsProviderToolCall(itemType)
           && (string.IsNullOrWhiteSpace(callId) || !outputCallIds.Contains(callId!));

    private static bool IsProviderToolCall(string? itemType)
        => string.Equals(itemType, "function_call", StringComparison.Ordinal)
           || string.Equals(itemType, "custom_tool_call", StringComparison.Ordinal);

    private static bool IsProviderToolCallOutput(string? itemType)
        => string.Equals(itemType, "function_call_output", StringComparison.Ordinal)
           || string.Equals(itemType, "custom_tool_call_output", StringComparison.Ordinal);

    private static string? ReadJsonElementString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryReadDictionaryString(object item, string propertyName, out string? value)
    {
        value = null;
        switch (item)
        {
            case IReadOnlyDictionary<string, object?> readOnlyDictionary
                when readOnlyDictionary.TryGetValue(propertyName, out var rawReadOnlyValue):
                value = rawReadOnlyValue switch
                {
                    string text => text,
                    JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
                    _ => rawReadOnlyValue?.ToString(),
                };
                return true;
            case IDictionary<string, object?> dictionary
                when dictionary.TryGetValue(propertyName, out var rawValue):
                value = rawValue switch
                {
                    string text => text,
                    JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
                    _ => rawValue?.ToString(),
                };
                return true;
            default:
                return false;
        }
    }

    private static bool TryReadDictionaryValue(object item, string propertyName, out object? value)
    {
        value = null;
        switch (item)
        {
            case IReadOnlyDictionary<string, object?> readOnlyDictionary
                when readOnlyDictionary.TryGetValue(propertyName, out var rawReadOnlyValue):
                value = rawReadOnlyValue;
                return true;
            case IDictionary<string, object?> dictionary
                when dictionary.TryGetValue(propertyName, out var rawValue):
                value = rawValue;
                return true;
            case JsonElement element
                when element.ValueKind == JsonValueKind.Object
                     && element.TryGetProperty(propertyName, out var property):
                value = property.Clone();
                return true;
            default:
                return false;
        }
    }

    private static bool TryReadStringValue(object? rawValue, out string? value)
    {
        value = rawValue switch
        {
            string text => text,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            _ => null,
        };

        return value is not null;
    }

    private static object ReplaceDictionaryValue(object item, string propertyName, object? value)
    {
        if (item is IReadOnlyDictionary<string, object?> readOnlyDictionary)
        {
            var clone = new Dictionary<string, object?>(readOnlyDictionary, StringComparer.Ordinal)
            {
                [propertyName] = value,
            };
            return clone;
        }

        if (item is IDictionary<string, object?> dictionary)
        {
            var clone = new Dictionary<string, object?>(dictionary, StringComparer.Ordinal)
            {
                [propertyName] = value,
            };
            return clone;
        }

        if (item is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            var clone = JsonSerializer.Deserialize<Dictionary<string, object?>>(element.GetRawText())
                        ?? new Dictionary<string, object?>(StringComparer.Ordinal);
            clone[propertyName] = value;
            return clone;
        }

        return item;
    }

    private static List<object> MaterializeResponsesConversationInput(IReadOnlyList<ContextSegment> includedSegments)
    {
        return includedSegments
            .OrderBy(static segment => segment.Metadata.TryGetValue("responsesInputOrder", out var rawOrder)
                ? rawOrder switch
                {
                    int intOrder => intOrder,
                    long longOrder => longOrder,
                    double doubleOrder => doubleOrder,
                    _ => int.MaxValue,
                }
                : int.MaxValue)
            .Select(MaterializeResponsesSegment)
            .Where(static item => item is not null)
            .Cast<object>()
            .ToList();
    }

    private static object? MaterializeResponsesSegment(ContextSegment segment)
    {
        if (segment.StructuredContent is not null)
        {
            return segment.StructuredContent;
        }

        if (string.IsNullOrWhiteSpace(segment.Text))
        {
            return null;
        }

        var prefix = segment.Kind switch
        {
            ContextSegmentKind.MemoryOverlay => "Memory overlay",
            ContextSegmentKind.ArtifactSnippet => "Artifact snippet",
            ContextSegmentKind.HistoricalSummary => "Historical summary",
            ContextSegmentKind.ToolResult => "Tool result",
            _ => "Context",
        };
        return CreateResponsesMessage("user", "input_text", $"{prefix}:{Environment.NewLine}{segment.Text}");
    }

    public static object CreateResponsesMessage(string role, string contentType, string text)
    {
        return new Dictionary<string, object?>
        {
            ["type"] = "message",
            ["role"] = role,
            ["content"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = contentType,
                    ["text"] = text,
                },
            },
        };
    }

    public static object CreateResponsesMessage(string role, IReadOnlyList<object> content)
    {
        return new Dictionary<string, object?>
        {
            ["type"] = "message",
            ["role"] = role,
            ["content"] = content.ToArray(),
        };
    }

    public static string NormalizeConversationRole(string? role)
    {
        var normalized = Normalize(role);
        return normalized?.ToLowerInvariant() switch
        {
            "system" => "system",
            "assistant" => "assistant",
            "developer" => "developer",
            _ => "user",
        };
    }

    public static string ExtractInputText(IEnumerable<JsonElement> inputItems)
    {
        var texts = new List<string>();
        foreach (var item in inputItems)
        {
            var directText = KernelRuntimeJsonHelpers.ReadString(item, "text");
            if (!string.IsNullOrWhiteSpace(directText))
            {
                texts.Add(directText!);
            }

            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var segment in content.EnumerateArray())
            {
                var nested = KernelRuntimeJsonHelpers.ReadString(segment, "text");
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    texts.Add(nested!);
                }
            }
        }

        return texts.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, texts);
    }

    public static string ExtractInputText(IEnumerable<KernelTurnInputItem> inputItems)
    {
        var texts = new List<string>();
        foreach (var item in inputItems)
        {
            if (!string.IsNullOrWhiteSpace(item.Text))
            {
                texts.Add(item.Text!);
            }

            if (item.ContentItems.Count > 0)
            {
                var nested = ExtractInputText(item.ContentItems);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    texts.Add(nested);
                }
            }
        }

        return texts.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, texts);
    }

    public static int CountInputTextChars(IReadOnlyList<JsonElement> inputItems)
    {
        var total = 0;
        foreach (var item in inputItems)
        {
            total += CountTextChars(KernelRuntimeJsonHelpers.ReadString(item, "text"));

            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var segment in content.EnumerateArray())
            {
                total += CountTextChars(KernelRuntimeJsonHelpers.ReadString(segment, "text"));
            }
        }

        return total;
    }

    public static int CountInputTextChars(IReadOnlyList<KernelTurnInputItem> inputItems)
    {
        var total = 0;
        foreach (var item in inputItems)
        {
            total += CountTextChars(item.Text);
            if (item.ContentItems.Count > 0)
            {
                total += CountInputTextChars(item.ContentItems);
            }
        }

        return total;
    }

    public static int CountTextChars(string? text)
        => string.IsNullOrEmpty(text) ? 0 : text.EnumerateRunes().Count();

    public static IReadOnlyList<KernelConversationHistoryItem> EnumerateTurnConversationHistoryItems(KernelTurnRecord turn)
    {
        var messages = new List<KernelConversationHistoryItem>();
        var sawUserMessageItem = false;
        var sawAssistantMessageItem = false;

        foreach (var item in turn.Items)
        {
            if (!TryBuildTurnConversationHistoryItem(item, out var historyItem))
            {
                continue;
            }

            var role = NormalizeConversationRole(historyItem.Role);
            if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
            {
                sawUserMessageItem = true;
            }
            else if (string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase))
            {
                sawAssistantMessageItem = true;
            }

            messages.Add(historyItem);
        }

        if (!sawUserMessageItem && !string.IsNullOrWhiteSpace(turn.UserMessage))
        {
            messages.Add(new KernelConversationHistoryItem
            {
                Role = "user",
                Content = turn.UserMessage,
            });
        }

        if (!sawAssistantMessageItem && !string.IsNullOrWhiteSpace(turn.AssistantMessage))
        {
            messages.Add(new KernelConversationHistoryItem
            {
                Role = "assistant",
                Content = turn.AssistantMessage,
            });
        }

        return NormalizeTurnConversationHistoryOrder(messages);
    }

    public static IReadOnlyList<KernelTurnRecord> SelectTurnsForPromptWindow(
        IReadOnlyList<KernelTurnRecord> turns,
        int maxTurns,
        string? currentUserText,
        IReadOnlyList<KernelTurnInputItem>? currentInputItems)
    {
        // 当前线程会在 turn/started 后先把 in-progress turn 写入 thread store。
        // 若该 turn 与本轮 current input 指向同一条用户输入，就不能再次回放，否则 provider prompt 会重复一次当前输入。
        // 但像 fork_context 这类“复制父线程实时上下文到子线程”的场景，仍需保留非终态 turn。
        // Current threads persist an in-progress turn into the thread store at turn/started time.
        // Skip replay only when that in-progress turn represents the same current input we append below;
        // keep other non-terminal turns, such as forked live parent context, inside the prompt window.
        var replayableTurns = turns
            .Where(turn => ShouldReplayTurnInPromptWindow(turn, currentUserText, currentInputItems))
            .ToArray();

        if (replayableTurns.Length <= maxTurns)
        {
            return replayableTurns;
        }

        return replayableTurns.TakeLast(maxTurns).ToArray();
    }

    private static string? SerializeEnvironmentContextSubagents(string? subagents)
    {
        var normalized = Normalize(subagents);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var lines = new List<string>
        {
            "<environment_context>",
            "  <subagents>",
        };
        lines.AddRange(normalized!
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static line => $"    {line}"));
        lines.Add("  </subagents>");
        lines.Add("</environment_context>");
        return string.Join(Environment.NewLine, lines);
    }

    private static string? ResolveCollaborationModeInstructions(
        KernelCollaborationModeState? state,
        TianShuPromptConfiguration? promptConfiguration,
        bool defaultModeRequestUserInputEnabled = false)
    {
        var explicitInstructions = Normalize(state?.Settings.DeveloperInstructions);
        if (!string.IsNullOrWhiteSpace(explicitInstructions))
        {
            return explicitInstructions;
        }

        var builtIn = KernelCollaborationModePrompts.ResolveDeveloperInstructions(state, defaultModeRequestUserInputEnabled);
        var mode = Normalize(state?.Mode) ?? KernelCollaborationModeState.DefaultMode;
        var section = string.Equals(mode, KernelCollaborationModeState.PlanMode, StringComparison.OrdinalIgnoreCase)
            ? promptConfiguration?.CollaborationPlan
            : promptConfiguration?.CollaborationDefault;
        return TianShuPromptConfigUtilities.ApplySection(section, builtIn);
    }

    private static void AppendInstructionSection(List<string> sections, string? value)
    {
        var normalized = Normalize(value);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            sections.Add(normalized!);
        }
    }

    private static void AppendResponsesConversationHistoryItem(
        List<object> list,
        KernelConversationHistoryItem historyItem,
        bool includeProviderReplayArtifacts = false)
    {
        if (historyItem.RawResponseItem is { ValueKind: JsonValueKind.Object } rawResponseItem
            && KernelConversationHistoryUtilities.TryNormalizeRawResponseItemForReplay(rawResponseItem, out var normalizedRawResponseItem))
        {
            list.Add(normalizedRawResponseItem.Clone());
            return;
        }

        var role = NormalizeConversationRole(historyItem.Role);
        if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase)
            && historyItem.Inputs.Count > 0)
        {
            var structuredContent = KernelConversationHistoryUtilities.BuildProviderContentItems(
                historyItem.Inputs,
                KernelPromptContentFormat.Responses);
            if (structuredContent.Length > 0)
            {
                list.Add(CreateResponsesMessage(role, structuredContent));
                return;
            }
        }

        var content = KernelConversationHistoryUtilities.BuildDisplayText(historyItem);
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        var contentType = string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase)
            ? "output_text"
            : "input_text";
        var message = CreateResponsesMessage(role, contentType, content!);
        if (includeProviderReplayArtifacts
            && string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase)
            && historyItem.RawResponseItem is { ValueKind: JsonValueKind.Object } providerRawResponseItem
            && TryReadProviderReasoningContent(providerRawResponseItem, out var reasoningContent)
            && message is Dictionary<string, object?> messageObject)
        {
            messageObject["reasoning_content"] = reasoningContent;
        }

        list.Add(message);
    }

    private static void AppendProviderConversationHistoryItem(
        List<Dictionary<string, object?>> messages,
        KernelConversationHistoryItem historyItem,
        KernelPromptContentFormat contentFormat)
    {
        var role = NormalizeConversationRole(historyItem.Role);
        var structuredContent = string.Equals(role, "user", StringComparison.OrdinalIgnoreCase)
            ? KernelConversationHistoryUtilities.BuildProviderContentItems(historyItem.Inputs, contentFormat)
            : Array.Empty<object>();
        if (contentFormat != KernelPromptContentFormat.PlainText && structuredContent.Length > 0)
        {
            messages.Add(new Dictionary<string, object?>
            {
                ["role"] = role,
                ["content"] = structuredContent,
            });
            return;
        }

        var content = KernelConversationHistoryUtilities.BuildDisplayText(historyItem);
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        messages.Add(new Dictionary<string, object?>
        {
            ["role"] = role,
            ["content"] = content,
        });
    }

    private static bool TryReadProviderReasoningContent(JsonElement item, out string reasoningContent)
    {
        reasoningContent = string.Empty;
        foreach (var propertyName in new[] { "reasoning_content", "reasoning" })
        {
            if (item.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value.GetString()))
            {
                reasoningContent = value.GetString()!;
                return true;
            }
        }

        return false;
    }

    public static (IReadOnlyList<KernelConversationHistoryItem> SeedHistory, IReadOnlyList<KernelConversationHistoryItem> TailContextHistory) SplitTailContextHistory(
        IEnumerable<KernelConversationHistoryItem> historyItems)
    {
        var seedHistory = new List<KernelConversationHistoryItem>();
        var tailContextHistory = new List<KernelConversationHistoryItem>();
        foreach (var historyItem in historyItems)
        {
            if (KernelSubagentNotificationUtilities.IsNotificationHistoryItem(historyItem))
            {
                tailContextHistory.Add(historyItem);
                continue;
            }

            seedHistory.Add(historyItem);
        }

        return (seedHistory, tailContextHistory);
    }

    private static bool ShouldReplayTurnInPromptWindow(
        KernelTurnRecord turn,
        string? currentUserText,
        IReadOnlyList<KernelTurnInputItem>? currentInputItems)
    {
        if (IsTerminalPersistedTurnStatus(turn.Status))
        {
            return true;
        }

        var currentPromptSignature = BuildPromptReplaySignature(currentUserText, currentInputItems);
        if (string.IsNullOrWhiteSpace(currentPromptSignature))
        {
            return true;
        }

        var turnPromptSignature = EnumerateTurnConversationHistoryItems(turn)
            .Where(static item => string.Equals(NormalizeConversationRole(item.Role), "user", StringComparison.OrdinalIgnoreCase))
            .Select(KernelConversationHistoryUtilities.BuildDisplayText)
            .Select(Normalize)
            .LastOrDefault(static text => !string.IsNullOrWhiteSpace(text));

        return !string.Equals(turnPromptSignature, currentPromptSignature, StringComparison.Ordinal);
    }

    private static string? BuildPromptReplaySignature(
        string? currentUserText,
        IReadOnlyList<KernelTurnInputItem>? currentInputItems)
    {
        var parsedInputs = KernelConversationHistoryUtilities.ParseInputItems(currentInputItems);
        if (parsedInputs.Count > 0)
        {
            return Normalize(KernelConversationHistoryUtilities.BuildInputPreview(parsedInputs));
        }

        return Normalize(currentUserText);
    }

    private static bool TryBuildTurnConversationHistoryItem(KernelTurnItemRecord item, out KernelConversationHistoryItem historyItem)
    {
        historyItem = null!;

        if (KernelUserShellRuntimeHelpers.TryBuildCommandHistoryText(item.Type, item.Payload, out var historyText))
        {
            historyItem = new KernelConversationHistoryItem
            {
                Role = "user",
                Content = historyText,
            };
            return true;
        }

        var normalizedType = Normalize(item.Type);
        var role = normalizedType?.ToLowerInvariant() switch
        {
            "usermessage" or "user_message" => "user",
            "agentmessage" or "assistant_message" => "assistant",
            _ => null,
        };
        if (role is not null)
        {
            var parsed = KernelConversationHistoryUtilities.ParseHistoryItem(item.Payload);
            if (parsed is null)
            {
                return false;
            }

            parsed.Role = role;
            if (!KernelConversationHistoryUtilities.HasMeaningfulContent(parsed))
            {
                return false;
            }

            historyItem = parsed;
            return true;
        }

        try
        {
            var parsed = KernelConversationHistoryUtilities.ParseHistoryItem(item.Payload, strictResponseItem: true);
            if (parsed is null
                || parsed.RawResponseItem is not { ValueKind: JsonValueKind.Object } rawResponseItem
                || !KernelConversationHistoryUtilities.TryNormalizeRawResponseItemForReplay(rawResponseItem, out _))
            {
                return false;
            }

            historyItem = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static IReadOnlyList<KernelConversationHistoryItem> NormalizeTurnConversationHistoryOrder(List<KernelConversationHistoryItem> messages)
    {
        if (messages.Count < 2)
        {
            return messages;
        }

        var firstUserIndex = -1;
        var firstAssistantIndex = -1;
        for (var index = 0; index < messages.Count; index++)
        {
            var role = NormalizeConversationRole(messages[index].Role);
            if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase) && firstUserIndex < 0)
            {
                firstUserIndex = index;
            }
            else if (string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase) && firstAssistantIndex < 0)
            {
                firstAssistantIndex = index;
            }

            if (firstUserIndex >= 0 && firstAssistantIndex >= 0)
            {
                break;
            }
        }

        if (firstUserIndex < 0 || firstAssistantIndex < 0 || firstAssistantIndex > firstUserIndex)
        {
            return messages;
        }

        var ordered = new List<KernelConversationHistoryItem>(messages.Count);
        ordered.AddRange(messages.Where(item => string.Equals(NormalizeConversationRole(item.Role), "user", StringComparison.OrdinalIgnoreCase)));
        ordered.AddRange(messages.Where(item => string.Equals(NormalizeConversationRole(item.Role), "assistant", StringComparison.OrdinalIgnoreCase)));
        ordered.AddRange(messages.Where(item =>
            !string.Equals(NormalizeConversationRole(item.Role), "user", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(NormalizeConversationRole(item.Role), "assistant", StringComparison.OrdinalIgnoreCase)));
        return ordered;
    }

    private static bool IsTerminalPersistedTurnStatus(string? status)
        => string.Equals(Normalize(status), "completed", StringComparison.OrdinalIgnoreCase)
           || string.Equals(Normalize(status), "failed", StringComparison.OrdinalIgnoreCase)
           || string.Equals(Normalize(status), "interrupted", StringComparison.OrdinalIgnoreCase);

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
