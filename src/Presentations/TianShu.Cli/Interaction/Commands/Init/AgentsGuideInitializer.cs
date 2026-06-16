namespace TianShu.Cli.Interaction.Commands.Init;

/// <summary>
/// Builds the `/init` AGENTS.md initialization request without touching runtime orchestration.
/// 构造 `/init` 的 AGENTS.md 初始化请求，但不参与 runtime 编排。
/// </summary>
internal sealed class AgentsGuideInitializer
{
    public const string AgentsGuideFileName = "AGENTS.md";

    public static readonly string InitPrompt = string.Join('\n',
    [
        "Generate a file named AGENTS.md that serves as a contributor guide for this repository.",
        "Your goal is to produce a clear, concise, and well-structured document with descriptive headings and actionable explanations for each section.",
        "Follow the outline below, but adapt as needed — add sections if relevant, and omit those that do not apply to this project.",
        string.Empty,
        "Document Requirements",
        string.Empty,
        "- Title the document \"Repository Guidelines\".",
        "- Use Markdown headings (#, ##, etc.) for structure.",
        "- Keep the document concise. 200-400 words is optimal.",
        "- Keep explanations short, direct, and specific to this repository.",
        "- Provide examples where helpful (commands, directory paths, naming patterns).",
        "- Maintain a professional, instructional tone.",
        string.Empty,
        "Recommended Sections",
        string.Empty,
        "Project Structure & Module Organization",
        string.Empty,
        "- Outline the project structure, including where the source code, tests, and assets are located.",
        string.Empty,
        "Build, Test, and Development Commands",
        string.Empty,
        "- List key commands for building, testing, and running locally (e.g., npm test, make build).",
        "- Briefly explain what each command does.",
        string.Empty,
        "Coding Style & Naming Conventions",
        string.Empty,
        "- Specify indentation rules, language-specific style preferences, and naming patterns.",
        "- Include any formatting or linting tools used.",
        string.Empty,
        "Testing Guidelines",
        string.Empty,
        "- Identify testing frameworks and coverage requirements.",
        "- State test naming conventions and how to run tests.",
        string.Empty,
        "Commit & Pull Request Guidelines",
        string.Empty,
        "- Summarize commit message conventions found in the project’s Git history.",
        "- Outline pull request requirements (descriptions, linked issues, screenshots, etc.).",
        string.Empty,
        "(Optional) Add other sections if relevant, such as Security & Configuration Tips, Architecture Overview, or Agent-Specific Instructions.",
    ]);

    public AgentsGuideInitializationRequest BuildRequest(string workingDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        var targetPath = Path.Combine(workingDirectory, AgentsGuideFileName);
        if (File.Exists(targetPath))
        {
            return new AgentsGuideInitializationRequest(
                targetPath,
                ShouldSubmitPrompt: false,
                Prompt: null,
                Message: "AGENTS.md already exists here. Skipping /init to avoid overwriting it.");
        }

        return new AgentsGuideInitializationRequest(
            targetPath,
            ShouldSubmitPrompt: true,
            Prompt: InitPrompt,
            Message: null);
    }
}

/// <summary>
/// Describes whether `/init` should submit the AGENTS.md prompt.
/// 描述 `/init` 是否应提交 AGENTS.md 初始化提示。
/// </summary>
internal sealed record AgentsGuideInitializationRequest(
    string TargetPath,
    bool ShouldSubmitPrompt,
    string? Prompt,
    string? Message);
