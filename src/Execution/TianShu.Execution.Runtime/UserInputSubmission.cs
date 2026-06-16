namespace TianShu.Execution.Runtime;

internal sealed record UserInputSubmission(
    IReadOnlyDictionary<string, AgentStructuredValue> Answers)
{
    public static UserInputSubmission Empty { get; } = new(
        new Dictionary<string, AgentStructuredValue>(StringComparer.Ordinal));
}
