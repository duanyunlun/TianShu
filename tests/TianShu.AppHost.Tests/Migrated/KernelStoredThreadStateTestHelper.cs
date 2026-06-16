using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.AppHost.Tests;

internal static class KernelStoredThreadStateTestHelper
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static KernelStoredThreadStateRecord FromThread(KernelThreadRecord thread)
    {
        ArgumentNullException.ThrowIfNull(thread);

        return new KernelStoredThreadStateRecord(
            ThreadId: thread.Id,
            Cwd: thread.Cwd ?? string.Empty,
            CreatedAt: thread.CreatedAt,
            UpdatedAt: thread.UpdatedAt,
            StatusType: thread.StatusType,
            IsArchived: thread.IsArchived,
            Name: thread.Name,
            PayloadJson: JsonSerializer.Serialize(thread, SerializerOptions));
    }

    public static KernelThreadRecord DeserializePayload(KernelStoredThreadStateRecord stored)
    {
        ArgumentNullException.ThrowIfNull(stored);

        return JsonSerializer.Deserialize<KernelThreadRecord>(stored.PayloadJson, SerializerOptions)
            ?? throw new InvalidOperationException("sqlite 线程镜像 payload 反序列化失败。");
    }
}
