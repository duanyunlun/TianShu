using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.Execution.Runtime.Tests;

public sealed class KernelThreadManagerTests
{
    [Fact]
    public void KernelRuntimeThread_ShouldMaintainRuntimeState()
    {
        var record = CreateRecord("thread_runtime_001", "D:/Repo");
        var session = CreateSession("D:/Repo", "on-request");
        var thread = new KernelRuntimeThread(record, session, isLoaded: true);

        var realtimeSession = new KernelRealtimeSessionState(record.Id, "realtime_001", string.Empty, null, null);
        thread.SetActiveTurn("turn_001");
        thread.SetRealtimeSession(realtimeSession);
        thread.UpdateSession(session with { ApprovalPolicy = "never" });
        thread.MarkUnloaded();
        thread.MarkLoaded();

        Assert.Equal(record.Id, thread.Id);
        Assert.True(thread.IsLoaded);
        Assert.Equal("turn_001", thread.ActiveTurnId);
        Assert.Same(realtimeSession, thread.RealtimeSession);
        Assert.Equal("never", thread.Session.ApprovalPolicy);

        var updatedRecord = CreateRecord(record.Id, "D:/Repo/Sub");
        thread.Update(updatedRecord);

        Assert.Equal("D:/Repo/Sub", thread.Record.Cwd);

        Assert.True(thread.ClearActiveTurn("turn_001"));
        Assert.Null(thread.ActiveTurnId);
    }

    [Fact]
    public void KernelRuntimeThread_ShouldExposeConfigSnapshot()
    {
        var session = CreateSession("D:/Repo", "never");
        var thread = new KernelRuntimeThread(CreateRecord("thread_snapshot_001", "D:/Repo"), session, isLoaded: true);

        var snapshot = thread.ConfigSnapshot;

        Assert.Equal("gpt-5", snapshot.Model);
        Assert.Equal("openai", snapshot.ModelProviderId);
        Assert.Equal("never", snapshot.ApprovalPolicy);
        Assert.Equal("D:/Repo", snapshot.Cwd);
        Assert.Equal("workspaceWrite", ((JsonElement)snapshot.SandboxPolicy).GetProperty("type").GetString());
        Assert.Equal("appServer", snapshot.SessionSource);
    }
    [Fact]
    public async Task KernelThreadManager_ShouldBroadcastCreatedThreadsAndTrackLoadedIds()
    {
        using var sandbox = new TestDirectoryScope();
        using var manager = CreateManager(sandbox.TianShuHome);
        var subscriberA = manager.SubscribeCreated();
        var subscriberB = manager.SubscribeCreated();
        var record = CreateRecord("thread_created_001", sandbox.WorkspaceRoot);
        var session = CreateSession(sandbox.WorkspaceRoot, "never");

        var thread = manager.AttachThread(record, session, loaded: true, publishCreated: true);

        var createdA = await subscriberA.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        var createdB = await subscriberB.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(record.Id, createdA);
        Assert.Equal(record.Id, createdB);
        Assert.True(manager.IsLoaded(record.Id));
        Assert.Contains(record.Id, manager.GetLoadedThreadIds());
        Assert.True(manager.TryGetThread(record.Id, out var trackedThread));
        Assert.Same(thread, trackedThread);
    }

    [Fact]
    public async Task KernelFileWatcher_ShouldEmitSkillsChangedForThreadRoots()
    {
        using var sandbox = new TestDirectoryScope();
        Directory.CreateDirectory(Path.Combine(sandbox.TianShuHome, "modules", "skills", "global-skill"));
        Directory.CreateDirectory(Path.Combine(sandbox.WorkspaceRoot, ".tianshu", "skills", "workspace-skill"));

        using var watcher = new KernelFileWatcher(sandbox.TianShuHome, TimeSpan.FromMilliseconds(100));
        using var registration = watcher.RegisterThread(CreateSession(sandbox.WorkspaceRoot, "on-request"));
        var subscriber = watcher.Subscribe();
        var skillFile = Path.Combine(sandbox.WorkspaceRoot, ".tianshu", "skills", "workspace-skill", "SKILL.md");

        await File.WriteAllTextAsync(skillFile, "# demo skill");

        var evt = await subscriber.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(KernelFileWatcherEventKind.SkillsChanged, evt.Kind);
        Assert.Contains(Path.GetFullPath(skillFile), evt.Paths);
        Assert.Contains(Path.GetFullPath(Path.Combine(sandbox.TianShuHome, "modules", "skills")), registration.Roots);
        Assert.Contains(Path.GetFullPath(Path.Combine(sandbox.WorkspaceRoot, ".tianshu", "skills")), registration.Roots);
    }

    [Fact]
    public void KernelFileWatcher_ShouldUseCustomSkillRootResolver()
    {
        using var sandbox = new TestDirectoryScope();
        var pluginSkillsRoot = Path.Combine(sandbox.Root, "plugin-skills");
        Directory.CreateDirectory(pluginSkillsRoot);

        using var watcher = new KernelFileWatcher(
            sandbox.TianShuHome,
            TimeSpan.FromMilliseconds(100),
            _ => new[]
            {
                pluginSkillsRoot,
            });
        using var registration = watcher.RegisterThread(CreateSession(sandbox.WorkspaceRoot, "on-request"));

        Assert.Contains(Path.GetFullPath(pluginSkillsRoot), registration.Roots);
        Assert.Equal(1, watcher.GetSkillsRootReferenceCount(pluginSkillsRoot));
    }

    [Fact]
    public void KernelFileWatcher_ShouldIncludeSystemAndAdminSkillRootsByDefault()
    {
        using var sandbox = new TestDirectoryScope();
        var systemConfigRoot = Path.Combine(sandbox.Root, "etc", "tianshu");
        using var watcher = new KernelFileWatcher(
            sandbox.TianShuHome,
            TimeSpan.FromMilliseconds(100),
            systemConfigRoot: systemConfigRoot);
        using var registration = watcher.RegisterThread(CreateSession(sandbox.WorkspaceRoot, "on-request"));

        Assert.Contains(Path.GetFullPath(Path.Combine(sandbox.TianShuHome, "modules", "skills", ".system")), registration.Roots);
        Assert.Contains(Path.GetFullPath(Path.Combine(systemConfigRoot, "skills")), registration.Roots);
    }

    [Fact]
    public void KernelThreadManager_ShouldRegisterAndReleaseThreadSkillRootsAcrossLoadLifecycle()
    {
        using var sandbox = new TestDirectoryScope();
        var globalSkillsRoot = Path.Combine(sandbox.TianShuHome, "modules", "skills");
        var workspaceSkillsRoot = Path.Combine(sandbox.WorkspaceRoot, ".tianshu", "skills");
        Directory.CreateDirectory(globalSkillsRoot);
        Directory.CreateDirectory(workspaceSkillsRoot);

        using var manager = CreateManager(sandbox.TianShuHome, TimeSpan.FromMilliseconds(100));
        var record = CreateRecord("thread_watch_001", sandbox.WorkspaceRoot);
        var session = CreateSession(sandbox.WorkspaceRoot, "on-request");

        var thread = manager.AttachThread(record, session, loaded: true, publishCreated: false);

        Assert.Contains(Path.GetFullPath(globalSkillsRoot), thread.WatchedSkillsRoots);
        Assert.Contains(Path.GetFullPath(workspaceSkillsRoot), thread.WatchedSkillsRoots);

        manager.MarkUnloaded(record.Id);

        Assert.Empty(thread.WatchedSkillsRoots);

        manager.MarkLoaded(record.Id);

        Assert.Contains(Path.GetFullPath(globalSkillsRoot), thread.WatchedSkillsRoots);
        Assert.Contains(Path.GetFullPath(workspaceSkillsRoot), thread.WatchedSkillsRoots);
        var watcher = manager.FileWatcher;
        Assert.Equal(1, watcher.GetSkillsRootReferenceCount(globalSkillsRoot));
        Assert.Equal(1, watcher.GetSkillsRootReferenceCount(workspaceSkillsRoot));

        manager.MarkUnloaded(record.Id);

        Assert.Equal(0, watcher.GetSkillsRootReferenceCount(globalSkillsRoot));
        Assert.Equal(0, watcher.GetSkillsRootReferenceCount(workspaceSkillsRoot));
    }

    private static KernelThreadManager CreateManager(string tianShuHome, TimeSpan? throttleInterval = null)
    {
        var watcher = new KernelFileWatcher(tianShuHome, throttleInterval ?? TimeSpan.FromMilliseconds(100));
        return new KernelThreadManager(watcher);
    }

    private static KernelThreadRecord CreateRecord(string id, string cwd)
    {
        return new KernelThreadRecord
        {
            Id = id,
            Cwd = cwd,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            StatusType = "idle",
            ActiveFlags = [],
            Turns = [],
        };
    }

    private static KernelThreadSessionState CreateSession(string cwd, string approvalPolicy)
    {
        var sandboxPolicy = JsonSerializer.SerializeToElement(new
        {
            type = "workspaceWrite",
            writableRoots = Array.Empty<string>(),
            readOnlyAccess = new { type = "fullAccess" },
            networkAccess = false,
            excludeTmpdirEnvVar = false,
            excludeSlashTmp = false,
        });

        return new KernelThreadSessionState(
            Model: "gpt-5",
            ModelProvider: "openai",
            ServiceTier: null,
            Cwd: cwd,
            ApprovalPolicy: approvalPolicy,
            SandboxPolicy: sandboxPolicy,
            SandboxMode: "workspaceWrite",
            Ephemeral: false,
            ServiceName: null,
            BaseInstructions: "base",
            DeveloperInstructions: "dev",
            Personality: null,
            DynamicTools: null,
            ProviderBaseUrl: null,
            ProviderApiKeyEnvironmentVariable: null,
            ProviderWireApi: null,
            SessionSource: KernelSessionSource.AppServer);
    }

    private sealed class TestDirectoryScope : IDisposable
    {
        public TestDirectoryScope()
        {
            Root = Path.Combine(Path.GetTempPath(), "tianshu-kernel-tests", Guid.NewGuid().ToString("N"));
            TianShuHome = Path.Combine(Root, ".tianshu-home");
            WorkspaceRoot = Path.Combine(Root, "workspace");
            Directory.CreateDirectory(TianShuHome);
            Directory.CreateDirectory(WorkspaceRoot);
        }

        public string Root { get; }

        public string TianShuHome { get; }

        public string WorkspaceRoot { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch
            {
                // ignore cleanup failures in tests
            }
        }
    }
}
