using System.Text.Json;

namespace TianShu.Execution.Protocol;

internal enum AppServerIncomingKind
{
    Unknown = 0,
    RpcResponse = 1,
    ServerRequest = 2,
    Notification = 3,
}

internal sealed record AppServerRpcErrorEnvelope(
    int Code,
    string Message,
    JsonElement? Data);

internal sealed record AppServerRpcResponseEnvelope(
    long Id,
    JsonElement Root,
    JsonElement? Result,
    AppServerRpcErrorEnvelope? Error,
    string RawJson);

internal sealed record AppServerServerRequestEnvelope(
    string Method,
    JsonElement Id,
    JsonElement Params,
    JsonElement Root,
    string RawJson);

internal sealed record AppServerNotificationEnvelope(
    string Method,
    JsonElement Params,
    JsonElement Root,
    string RawJson);

internal sealed record AppServerIncomingEnvelope(
    AppServerIncomingKind Kind,
    AppServerRpcResponseEnvelope? RpcResponse = null,
    AppServerServerRequestEnvelope? ServerRequest = null,
    AppServerNotificationEnvelope? Notification = null);
