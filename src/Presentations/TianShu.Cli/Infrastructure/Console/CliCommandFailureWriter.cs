using System.Text.Json;
using TianShu.Execution.Runtime;

namespace TianShu.Cli;

internal static class CliCommandFailureWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static bool TryWriteStructuredFailure(object? command, Exception exception)
    {
        if (exception is not AppServerRpcException rpcException)
        {
            return false;
        }

        if (ShouldWriteJson(command))
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(BuildErrorEnvelope(rpcException), JsonOptions));
        }
        else
        {
            Console.Error.WriteLine(FormatText(rpcException));
        }

        return true;
    }

    internal static object BuildErrorEnvelope(AppServerRpcException exception)
    {
        var error = new Dictionary<string, object?>
        {
            ["code"] = exception.Code,
            ["message"] = exception.RpcMessage,
        };
        if (exception.ErrorData is not null)
        {
            error["data"] = exception.ErrorData.ToPlainObject();
        }

        return new Dictionary<string, object?>
        {
            ["error"] = error,
        };
    }

    internal static string FormatText(AppServerRpcException exception)
    {
        if (exception.ErrorData is null)
        {
            return exception.Message;
        }

        return $"{exception.Message}{Environment.NewLine}{JsonSerializer.Serialize(exception.ErrorData.ToPlainObject(), JsonOptions)}";
    }

    private static bool ShouldWriteJson(object? command)
    {
        return command switch
        {
            RpcCommandOptions => true,
            SendCommandOptions options => options.OutputJson,
            FollowUpCliCommandOptions options => options.OutputJson,
            ThreadCommandOptions options => options.OutputJson,
            RuntimeSurfaceCommandOptions options => options.OutputJson,
            CommandExecCommandOptions options => options.OutputJson,
            ExecCommandOptions options => options.OutputJson,
            CodeModeCommandOptions options => options.OutputJson,
            FuzzyFileSearchCommandOptions options => options.OutputJson,
            FeedbackCommandOptions options => options.OutputJson,
            WindowsSandboxCommandOptions options => options.OutputJson,
            RealtimeCommandOptions options => options.OutputJson,
            ChatCommandOptions options => options.OutputProtocol == ChatOutputProtocol.Jsonl,
            _ => false,
        };
    }
}
