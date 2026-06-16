namespace Windows.Agent.Contracts;

public sealed class UiContractValidationResult
{
    public bool IsValid => Errors.Count == 0;

    public List<string> Errors { get; } = new();

    public List<string> Warnings { get; } = new();
}

