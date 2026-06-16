namespace TianShu.AppHost.Tools.Runtime;

internal enum ContextSourceKind
{
    Instruction,
    UserInput,
    ConversationHistory,
    ToolOutput,
    Memory,
    Artifact,
    Environment,
    Summary,
    Recovery,
}

internal enum ContextSegmentKind
{
    CoreInstruction,
    DeveloperInstruction,
    CurrentUserInput,
    CurrentPlan,
    RecentTurn,
    ToolResult,
    MemoryOverlay,
    ArtifactSnippet,
    EnvironmentContext,
    HistoricalSummary,
    RecoveryHint,
}

internal enum ContextSegmentPriority
{
    Critical,
    High,
    MediumHigh,
    Medium,
    Low,
}

internal enum ContextRetentionPolicy
{
    MustKeep,
    KeepIfRelevant,
    SummarizeIfDropped,
    ReferenceOnlyIfDropped,
    DropFirst,
}

internal enum DroppedContextReason
{
    BudgetExceeded,
    LowerPriority,
    SupersededBySummary,
    LowRelevance,
    PolicyExcluded,
    Duplicate,
    ProviderLimit,
    SensitiveOrUnsafe,
    ConflictsWithCurrentGoal,
}

internal enum OverBudgetReasonCode
{
    DefaultBudgetExceeded,
    UserRequestedLongContext,
    LargeCodeReview,
    FullLogAnalysis,
    ProviderLimitReached,
    EmergencyRecovery,
}
