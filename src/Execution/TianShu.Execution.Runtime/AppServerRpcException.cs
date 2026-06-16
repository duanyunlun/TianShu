using TianShu.Contracts.Primitives;

namespace TianShu.Execution.Runtime;

public sealed class AppServerRpcException : InvalidOperationException
{
    public AppServerRpcException(int code, string rpcMessage, StructuredValue? errorData = null)
        : base($"app-server 返回错误：{rpcMessage}")
    {
        Code = code;
        RpcMessage = rpcMessage;
        ErrorData = errorData;
    }

    public int Code { get; }

    public string RpcMessage { get; }

    public StructuredValue? ErrorData { get; }
}
