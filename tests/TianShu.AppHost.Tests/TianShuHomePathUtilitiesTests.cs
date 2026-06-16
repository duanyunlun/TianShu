using TianShu.AppHost.Configuration;

namespace TianShu.AppHost.Tests;

public sealed class TianShuHomePathUtilitiesTests
{
    [Fact]
    public void ResolveTianShuHomePath_ShouldUseTianShuHome()
    {
        var original = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var expected = Path.Combine(Path.GetTempPath(), $"tianshu-home-{Guid.NewGuid():N}");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", expected);

            var resolved = TianShuHomePathUtilities.ResolveTianShuHomePath();

            Assert.Equal(expected, resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", original);
        }
    }

    [Fact]
    public void ResolveTianShuHomePath_ShouldIgnoreExternalAgentHome()
    {
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var originalExternalAgentHome = Environment.GetEnvironmentVariable("CODEX_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", null);
            Environment.SetEnvironmentVariable("CODEX_HOME", Path.Combine(Path.GetTempPath(), $"external-agent-home-{Guid.NewGuid():N}"));

            var resolved = TianShuHomePathUtilities.ResolveTianShuHomePath();
            var expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".tianshu");

            Assert.Equal(expected, resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            Environment.SetEnvironmentVariable("CODEX_HOME", originalExternalAgentHome);
        }
    }

    [Fact]
    public void ResolveTianShuHomePath_ShouldFallBackToUserProfileDotTianShu()
    {
        var original = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", null);

            var resolved = TianShuHomePathUtilities.ResolveTianShuHomePath();
            var expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".tianshu");

            Assert.Equal(expected, resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", original);
        }
    }

    [Fact]
    public void ResolveTianShuStateRootPath_ShouldUseTianShuStateHome()
    {
        var original = Environment.GetEnvironmentVariable("TIANSHU_STATE_HOME");
        var expected = Path.Combine(Path.GetTempPath(), $"tianshu-state-home-{Guid.NewGuid():N}");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", expected);

            var resolved = TianShuHomePathUtilities.ResolveTianShuStateRootPath();

            Assert.Equal(expected, resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", original);
        }
    }

    [Fact]
    public void ResolveTianShuStateRootPath_ShouldIgnoreOldKernelStateHome()
    {
        var originalStateHome = Environment.GetEnvironmentVariable("TIANSHU_STATE_HOME");
        var originalKernelHome = Environment.GetEnvironmentVariable("TIANSHU_KERNEL_HOME");
        var originalHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var homePath = Path.Combine(Path.GetTempPath(), $"tianshu-home-{Guid.NewGuid():N}");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", null);
            Environment.SetEnvironmentVariable("TIANSHU_KERNEL_HOME", Path.Combine(Path.GetTempPath(), $"kernel-home-{Guid.NewGuid():N}"));
            Environment.SetEnvironmentVariable("TIANSHU_HOME", homePath);

            var resolved = TianShuHomePathUtilities.ResolveTianShuStateRootPath();

            Assert.Equal(Path.Combine(homePath, "data", "state"), resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", originalStateHome);
            Environment.SetEnvironmentVariable("TIANSHU_KERNEL_HOME", originalKernelHome);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalHome);
        }
    }

    [Fact]
    public void ResolveTianShuStateRootPath_ShouldFallBackToTianShuHomeStateDirectory()
    {
        var originalStateHome = Environment.GetEnvironmentVariable("TIANSHU_STATE_HOME");
        var originalHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var homePath = Path.Combine(Path.GetTempPath(), $"tianshu-home-{Guid.NewGuid():N}");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", null);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", homePath);

            var resolved = TianShuHomePathUtilities.ResolveTianShuStateRootPath();

            Assert.Equal(Path.Combine(homePath, "data", "state"), resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", originalStateHome);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalHome);
        }
    }

    [Fact]
    public void ResolveTianShuSessionsRootPath_ShouldUseTianShuSessionsHome()
    {
        var original = Environment.GetEnvironmentVariable("TIANSHU_SESSIONS_HOME");
        var expected = Path.Combine(Path.GetTempPath(), $"tianshu-sessions-home-{Guid.NewGuid():N}");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_SESSIONS_HOME", expected);

            var resolved = TianShuHomePathUtilities.ResolveTianShuSessionsRootPath();

            Assert.Equal(expected, resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_SESSIONS_HOME", original);
        }
    }

    [Fact]
    public void ResolveTianShuSessionsRootPath_ShouldFallBackToTianShuHomeSessionsDirectory()
    {
        var originalSessionsHome = Environment.GetEnvironmentVariable("TIANSHU_SESSIONS_HOME");
        var originalHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var homePath = Path.Combine(Path.GetTempPath(), $"tianshu-home-{Guid.NewGuid():N}");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_SESSIONS_HOME", null);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", homePath);

            var resolved = TianShuHomePathUtilities.ResolveTianShuSessionsRootPath();

            Assert.Equal(Path.Combine(homePath, "data", "sessions"), resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_SESSIONS_HOME", originalSessionsHome);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalHome);
        }
    }
}

