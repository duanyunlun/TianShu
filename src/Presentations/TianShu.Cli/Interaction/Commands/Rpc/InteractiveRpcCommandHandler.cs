using System.Text.Json;
using TianShu.Contracts.Primitives;
using TianShu.Execution.Runtime;

namespace TianShu.Cli.Interaction.Commands.Rpc;

/// <summary>
/// Handles the interactive /rpc diagnostic command.
/// 处理交互式 /rpc 诊断命令。
/// </summary>
internal sealed class InteractiveRpcCommandHandler
{
    public async Task HandleRpcAsync(
        IExecutionRuntime runtime,
        string rest,
        JsonSerializerOptions jsonOptions,
        Action<string, bool> writeLine,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(jsonOptions);
        ArgumentNullException.ThrowIfNull(writeLine);

        var method = ReadFirstToken(rest, out var paramsJson);
        if (string.IsNullOrWhiteSpace(method))
        {
            writeLine("用法：/rpc <method> [params-json]", true);
            return;
        }

        StructuredValue? parameters = null;
        if (!string.IsNullOrWhiteSpace(paramsJson))
        {
            try
            {
                using var document = JsonDocument.Parse(paramsJson);
                parameters = StructuredValue.FromJsonElement(document.RootElement);
            }
            catch (JsonException ex)
            {
                writeLine($"解析 RPC 参数 JSON 失败：{ex.Message}", true);
                return;
            }
        }

        var formalDispatch = await CliRuntimeCommandRunner.TryInvokeFormalRpcAsync(runtime, method, parameters, cancellationToken).ConfigureAwait(false);
        if (formalDispatch.Handled)
        {
            writeLine(JsonSerializer.Serialize(formalDispatch.Result, jsonOptions), false);
            return;
        }

        writeLine(CliRuntimeCommandRunner.BuildFormalRpcUnavailableMessage(method), true);
    }

    private static string ReadFirstToken(string text, out string remainder)
    {
        text = text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            remainder = string.Empty;
            return string.Empty;
        }

        var index = text.IndexOf(' ', StringComparison.Ordinal);
        if (index < 0)
        {
            remainder = string.Empty;
            return text;
        }

        remainder = text[(index + 1)..].Trim();
        return text[..index];
    }
}
