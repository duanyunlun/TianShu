namespace TianShu.Provider.Abstractions;

/// <summary>
/// OpenAI / ChatGPT 应用目录互操作共享常量。
/// Shared constants for OpenAI / ChatGPT app-catalog interop.
/// </summary>
public static class OpenAiAppCatalogCompatibilityKeys
{
    public const string DefaultBaseUrl = "https://chatgpt.com";
    public const string CodexAppsMcpServerName = "codex_apps";
    public const string CodexAppsMcpPath = "api/codex/apps";
    public const string ChatGptBaseUrlConfigKey = "chatgpt_base_url";
    public const string ChatGptAccountIdHeaderName = "chatgpt-account-id";
    public const string ForcedChatGptWorkspaceIdConfigKey = "forced_chatgpt_workspace_id";
    public const string ForcedLoginMethodConfigKey = "forced_login_method";
    public const string RequiresOpenAiAuthConfigKey = "requires_openai_auth";
}
