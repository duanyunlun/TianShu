using System.Text.Json;

using System.Text.Json.Serialization;

namespace TianShu.Execution.Runtime;

internal sealed record KernelPendingFollowUpCompareKeyRecord(
    string? Message,
    int ImageCount)
{
    public KernelPendingFollowUpCompareKeyRecord DeepClone()
        => new(
            KernelRuntimeJsonHelpers.Normalize(Message),
            Math.Max(0, ImageCount));
}

internal sealed record KernelPendingByteRangeRecord(
    int Start,
    int End)
{
    public KernelPendingByteRangeRecord DeepClone()
        => new(
            Math.Max(0, Start),
            Math.Max(0, End));
}

internal sealed record KernelPendingTextElementRecord(
    KernelPendingByteRangeRecord? ByteRange,
    string? Placeholder)
{
    public KernelPendingTextElementRecord DeepClone()
        => new(
            ByteRange?.DeepClone(),
            KernelRuntimeJsonHelpers.Normalize(Placeholder));
}

internal sealed record KernelPendingUserInputRecord(
    string Type,
    string? Text = null,
    string? Url = null,
    string? Path = null,
    string? Name = null,
    IReadOnlyList<KernelPendingTextElementRecord>? TextElements = null)
{
    public KernelPendingUserInputRecord DeepClone()
        => new(
            KernelRuntimeJsonHelpers.Normalize(Type) ?? string.Empty,
            KernelRuntimeJsonHelpers.Normalize(Text),
            KernelRuntimeJsonHelpers.Normalize(Url),
            KernelRuntimeJsonHelpers.Normalize(Path),
            KernelRuntimeJsonHelpers.Normalize(Name),
            TextElements is null || TextElements.Count == 0
                ? Array.Empty<KernelPendingTextElementRecord>()
                : TextElements.Select(static item => item.DeepClone()).ToArray());
}

internal sealed record KernelPendingInputStateEntryRecord(
    string CorrelationId,
    string RequestedMode,
    string EffectiveMode,
    string LifecycleState,
    string? ExpectedTurnId,
    string? TurnId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    KernelPendingFollowUpCompareKeyRecord? CompareKey,
    string PendingBucket = "QueuedUserMessage",
    IReadOnlyList<KernelPendingUserInputRecord>? Inputs = null)
{
    public KernelPendingInputStateEntryRecord DeepClone()
        => new(
            KernelRuntimeJsonHelpers.Normalize(CorrelationId) ?? string.Empty,
            KernelRuntimeJsonHelpers.Normalize(RequestedMode) ?? string.Empty,
            KernelRuntimeJsonHelpers.Normalize(EffectiveMode) ?? string.Empty,
            KernelRuntimeJsonHelpers.Normalize(LifecycleState) ?? string.Empty,
            KernelRuntimeJsonHelpers.Normalize(ExpectedTurnId),
            KernelRuntimeJsonHelpers.Normalize(TurnId),
            null,
            KernelRuntimeJsonHelpers.Normalize(PendingBucket) ?? "QueuedUserMessage",
            Inputs is null || Inputs.Count == 0
                ? Array.Empty<KernelPendingUserInputRecord>()
                : Inputs.Select(static item => item.DeepClone()).ToArray());
}

internal sealed record KernelPendingInputStateRecord(
    IReadOnlyList<KernelPendingInputStateEntryRecord> Entries,
    bool InterruptRequestPending,
    bool SubmitPendingSteersAfterInterrupt = false,
    IReadOnlyList<KernelPendingInputStateEntryRecord>? QueuedUserMessages = null,
    IReadOnlyList<KernelPendingInputStateEntryRecord>? PendingSteers = null)
{
    public KernelPendingInputStateRecord DeepClone()
        => new(
            Entries.Count == 0
                ? Array.Empty<KernelPendingInputStateEntryRecord>()
                : Entries.Select(static entry => entry.DeepClone()).ToArray(),
            InterruptRequestPending,
            SubmitPendingSteersAfterInterrupt,
            QueuedUserMessages is null || QueuedUserMessages.Count == 0
                ? Array.Empty<KernelPendingInputStateEntryRecord>()
                : QueuedUserMessages.Select(static entry => entry.DeepClone()).ToArray(),
            PendingSteers is null || PendingSteers.Count == 0
                ? Array.Empty<KernelPendingInputStateEntryRecord>()
                : PendingSteers.Select(static entry => entry.DeepClone()).ToArray());
}

internal static class KernelPendingInputStateFactory
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public static KernelPendingInputStateRecord Empty { get; }
        = new(
            Array.Empty<KernelPendingInputStateEntryRecord>(),
            false,
            false,
            Array.Empty<KernelPendingInputStateEntryRecord>(),
            Array.Empty<KernelPendingInputStateEntryRecord>());

    public static bool TryRead(JsonElement json, out KernelPendingInputStateRecord? state)
    {
        state = null;
        if (json.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return true;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<KernelPendingInputStateRecord>(json.GetRawText(), SerializerOptions);
            state = Normalize(parsed);
            return true;
        }
        catch
        {
            state = null;
            return false;
        }
    }

    public static KernelPendingInputStateRecord? Normalize(KernelPendingInputStateRecord? state)
    {
        if (state is null)
        {
            return null;
        }

        var entries = NormalizeEntries(state.Entries);
        var queuedUserMessages = NormalizeEntries(state.QueuedUserMessages, forcedPendingBucket: "QueuedUserMessage");
        var pendingSteers = NormalizeEntries(state.PendingSteers, forcedPendingBucket: "PendingSteer");
        entries = FilterSupplementalEntries(entries);

        if (entries.Length == 0
            && queuedUserMessages.Length == 0
            && pendingSteers.Length == 0)
        {
            return null;
        }

        var hasPendingSteerEntries = pendingSteers.Length > 0;
        return new KernelPendingInputStateRecord(
            entries,
            state.InterruptRequestPending,
            state.SubmitPendingSteersAfterInterrupt && hasPendingSteerEntries,
            queuedUserMessages,
            pendingSteers);
    }

    public static KernelPendingInputStateRecord ToPersistedState(KernelPendingInputStateRecord? state)
        => Normalize(state) ?? Empty;

    private static KernelPendingInputStateEntryRecord[] NormalizeEntries(
        IReadOnlyList<KernelPendingInputStateEntryRecord>? entries,
        string? forcedPendingBucket = null)
        => (entries ?? Array.Empty<KernelPendingInputStateEntryRecord>())
            .Select(entry => Normalize(entry, forcedPendingBucket))
            .Where(static entry => entry is not null)
            .Cast<KernelPendingInputStateEntryRecord>()
            .ToArray();

    private static KernelPendingInputStateEntryRecord[] FilterSupplementalEntries(
        IReadOnlyList<KernelPendingInputStateEntryRecord> entries)
        => entries
            .Where(static entry =>
                !IsQueuedUserMessageEntry(entry)
                && !IsPendingSteerEntry(entry))
            .ToArray();

    private static bool IsInterruptPendingEntry(KernelPendingInputStateEntryRecord entry)
        => string.Equals(entry.RequestedMode, "Interrupt", StringComparison.OrdinalIgnoreCase)
           && (entry.LifecycleState is "queued" or "interrupt_requested"
               || string.Equals(entry.LifecycleState, "interrupt_completed", StringComparison.Ordinal));

    private static bool IsQueuedUserMessageEntry(KernelPendingInputStateEntryRecord entry)
        => string.Equals(entry.PendingBucket, "QueuedUserMessage", StringComparison.OrdinalIgnoreCase)
           && !IsInterruptPendingEntry(entry);

    private static bool IsPendingSteerEntry(KernelPendingInputStateEntryRecord entry)
        => string.Equals(entry.PendingBucket, "PendingSteer", StringComparison.OrdinalIgnoreCase);

    private static KernelPendingInputStateEntryRecord? Normalize(KernelPendingInputStateEntryRecord? entry, string? forcedPendingBucket = null)
    {
        if (entry is null)
        {
            return null;
        }

        var correlationId = KernelRuntimeJsonHelpers.Normalize(entry.CorrelationId);
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return null;
        }

        return new KernelPendingInputStateEntryRecord(
            correlationId!,
            KernelRuntimeJsonHelpers.Normalize(entry.RequestedMode) ?? string.Empty,
            KernelRuntimeJsonHelpers.Normalize(entry.EffectiveMode) ?? string.Empty,
            KernelRuntimeJsonHelpers.Normalize(entry.LifecycleState) ?? string.Empty,
            KernelRuntimeJsonHelpers.Normalize(entry.ExpectedTurnId),
            KernelRuntimeJsonHelpers.Normalize(entry.TurnId),
            null,
            forcedPendingBucket
            ?? KernelRuntimeJsonHelpers.Normalize(entry.PendingBucket)
            ?? "QueuedUserMessage",
            NormalizeInputs(entry.Inputs));
    }

    private static KernelPendingUserInputRecord[] NormalizeInputs(IReadOnlyList<KernelPendingUserInputRecord>? inputs)
        => (inputs ?? Array.Empty<KernelPendingUserInputRecord>())
            .Select(NormalizeInput)
            .Where(static item => item is not null)
            .Cast<KernelPendingUserInputRecord>()
            .ToArray();

    private static KernelPendingUserInputRecord? NormalizeInput(KernelPendingUserInputRecord? input)
    {
        if (input is null)
        {
            return null;
        }

        var normalizedType = KernelRuntimeJsonHelpers.Normalize(input.Type);
        if (string.IsNullOrWhiteSpace(normalizedType))
        {
            return null;
        }

        var normalizedTextElements = (input.TextElements ?? Array.Empty<KernelPendingTextElementRecord>())
            .Select(NormalizeTextElement)
            .Where(static item => item is not null)
            .Cast<KernelPendingTextElementRecord>()
            .ToArray();

        return new KernelPendingUserInputRecord(
            normalizedType!,
            KernelRuntimeJsonHelpers.Normalize(input.Text),
            KernelRuntimeJsonHelpers.Normalize(input.Url),
            KernelRuntimeJsonHelpers.Normalize(input.Path),
            KernelRuntimeJsonHelpers.Normalize(input.Name),
            normalizedTextElements);
    }

    private static KernelPendingTextElementRecord? NormalizeTextElement(KernelPendingTextElementRecord? element)
    {
        if (element is null)
        {
            return null;
        }

        return new KernelPendingTextElementRecord(
            element.ByteRange is null
                ? null
                : new KernelPendingByteRangeRecord(
                    Math.Max(0, element.ByteRange.Start),
                    Math.Max(0, element.ByteRange.End)),
            KernelRuntimeJsonHelpers.Normalize(element.Placeholder));
    }
}
