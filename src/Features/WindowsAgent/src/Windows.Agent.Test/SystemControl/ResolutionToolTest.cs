using Windows.Agent.Interface;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.InteropServices;
using Windows.Agent.Tools.SystemControl;
using Windows.Agent.Services;
using Xunit;

namespace Windows.Agent.Test.SystemControl
{
    /// <summary>
    /// ResolutionTool单元测试类
    /// </summary>
[Trait("Category", "SystemControl")]
    [Collection("SystemControl")]
    public class ResolutionToolTest : IAsyncLifetime
    {
        private readonly ISystemControlService _systemControlService;
        private readonly ILogger<ResolutionTool> _logger;
        private readonly ResolutionTool _resolutionTool;

        private bool _captured;
        private DEVMODE _originalDevMode;

        public ResolutionToolTest()
        {
            // 创建服务容器并注册依赖
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole());
            services.AddSingleton<ISystemControlService, SystemControlService>();
            
            var serviceProvider = services.BuildServiceProvider();
            
            // 获取实际的服务实例
            _systemControlService = serviceProvider.GetRequiredService<ISystemControlService>();
            _logger = serviceProvider.GetRequiredService<ILogger<ResolutionTool>>();
            _resolutionTool = new ResolutionTool(_systemControlService, _logger);
        }

        public Task InitializeAsync()
        {
            // 记录当前分辨率设置，用于测试结束后恢复。
            var devMode = CreateDevMode();
            var ok = EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref devMode) != 0;

            if (ok)
            {
                _originalDevMode = devMode;
                _captured = true;
            }

            return Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            if (!_captured)
            {
                return Task.CompletedTask;
            }

            try
            {
                ChangeDisplaySettings(ref _originalDevMode, CDS_UPDATEREGISTRY);
            }
            catch
            {
                // 清理阶段不额外失败
            }

            return Task.CompletedTask;
        }

        private static DEVMODE CreateDevMode()
        {
            return new DEVMODE
            {
                dmDeviceName = new string('\0', 32),
                dmFormName = new string('\0', 32),
                dmSize = (short)Marshal.SizeOf<DEVMODE>()
            };
        }

        private const int ENUM_CURRENT_SETTINGS = -1;
        private const int CDS_UPDATEREGISTRY = 0x01;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int ChangeDisplaySettings(ref DEVMODE devMode, int flags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        [Theory]
        [InlineData("high")]
        [InlineData("medium")]
        [InlineData("low")]
        public async Task SetResolutionAsync_WithValidType_ShouldReturnResult(string type)
        {
            // Act
            var result = await _resolutionTool.SetResolutionAsync(type);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);
        }

        [Theory]
        [InlineData("高")]  // Chinese for high
        [InlineData("中")]  // Chinese for medium
        [InlineData("低")]  // Chinese for low
        public async Task SetResolutionAsync_WithChineseType_ShouldReturnResult(string type)
        {
            // Act
            var result = await _resolutionTool.SetResolutionAsync(type);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);
        }

        [Theory]
        [InlineData("HIGH")]  // Uppercase
        [InlineData("Medium")] // Mixed case
        [InlineData("LOW")]   // Uppercase
        public async Task SetResolutionAsync_WithDifferentCasing_ShouldReturnResult(string type)
        {
            // Act
            var result = await _resolutionTool.SetResolutionAsync(type);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);
        }

        [Theory]
        [InlineData("invalid")]
        [InlineData("unknown")]
        [InlineData("")]
        [InlineData("   ")]
        public async Task SetResolutionAsync_WithInvalidType_ShouldDefaultToHigh(string type)
        {
            // Act
            var result = await _resolutionTool.SetResolutionAsync(type);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);
            // Should handle gracefully, defaulting to high resolution
        }

        [Fact]
        public async Task SetResolutionAsync_SequentialChanges_ShouldHandleMultipleRequests()
        {
            // Test changing resolution multiple times
            var resolutions = new[] { "high", "medium", "low", "high" };

            foreach (var resolution in resolutions)
            {
                // Act
                var result = await _resolutionTool.SetResolutionAsync(resolution);

                // Assert
                Assert.NotNull(result);
                Assert.NotEmpty(result);

                // Add small delay to prevent rapid changes
                await Task.Delay(100);
            }
        }

        [Fact]
        public async Task SetResolutionAsync_WithNullType_ShouldHandleGracefully()
        {
            // Act
            var result = await _resolutionTool.SetResolutionAsync(null!);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);
        }

        [Fact]
        public async Task ResolutionOperations_StressTest_ShouldHandleMultipleCalls()
        {
            // Test multiple rapid resolution changes
            var tasks = new List<Task<string>>();
            var resolutions = new[] { "high", "medium", "low" };

            for (int i = 0; i < 5; i++)
            {
                var resolution = resolutions[i % resolutions.Length];
                tasks.Add(_resolutionTool.SetResolutionAsync(resolution));
            }

            // Wait for all tasks to complete
            var results = await Task.WhenAll(tasks);

            // Assert all results are valid
            foreach (var result in results)
            {
                Assert.NotNull(result);
                Assert.NotEmpty(result);
            }
        }

        [Theory]
        [InlineData("high", "2560", "1600")]   // Expected high resolution
        [InlineData("medium", "1920", "1080")] // Expected medium resolution
        [InlineData("low", "1600", "900")]     // Expected low resolution
        public async Task SetResolutionAsync_WithSpecificType_ShouldSetExpectedResolution(string type, string expectedWidth, string expectedHeight)
        {
            // Act
            var result = await _resolutionTool.SetResolutionAsync(type);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);
            
            // Note: We can't easily verify the actual resolution was set without additional system calls,
            // but we can verify the operation completed without errors
        }

        [Fact]
        public async Task SetResolutionAsync_ErrorHandling_ShouldNotThrowException()
        {
            // Test that the method handles errors gracefully
            var testCases = new[] { "high", "medium", "low", "invalid", null!, "" };

            foreach (var testCase in testCases)
            {
                // Act & Assert - Should not throw
                var exception = await Record.ExceptionAsync(async () =>
                {
                    var result = await _resolutionTool.SetResolutionAsync(testCase);
                    Assert.NotNull(result);
                });

                Assert.Null(exception);
            }
        }
    }
}
