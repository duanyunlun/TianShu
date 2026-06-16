using Windows.Agent.Interface;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using NAudio.CoreAudioApi;
using Windows.Agent.Tools.SystemControl;
using Windows.Agent.Services;
using Xunit;

namespace Windows.Agent.Test.SystemControl
{
    /// <summary>
    /// VolumeTool单元测试类
    /// </summary>
[Trait("Category", "SystemControl")]
    [Collection("SystemControl")]
    public class VolumeToolTest : IAsyncLifetime
    {
        private readonly ISystemControlService _systemControlService;
        private readonly ILogger<VolumeTool> _logger;
        private readonly VolumeTool _volumeTool;

        private bool _captured;
        private float _originalVolumeScalar;
        private bool _originalMute;

        public VolumeToolTest()
        {
            // 创建服务容器并注册依赖
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole());
            services.AddSingleton<ISystemControlService, SystemControlService>();
            
            var serviceProvider = services.BuildServiceProvider();
            
            // 获取实际的服务实例
            _systemControlService = serviceProvider.GetRequiredService<ISystemControlService>();
            _logger = serviceProvider.GetRequiredService<ILogger<VolumeTool>>();
            _volumeTool = new VolumeTool(_systemControlService, _logger);
        }

        public async Task InitializeAsync()
        {
            // 记录测试开始前的系统状态，测试结束后尽量恢复（即使测试失败也会进入 DisposeAsync）。
            _originalVolumeScalar = await _systemControlService.GetCurrentVolumeAsync();
            _originalMute = GetCurrentMute();
            _captured = true;
        }

        public async Task DisposeAsync()
        {
            if (!_captured)
            {
                return;
            }

            try
            {
                var percent = (int)Math.Round(Math.Clamp(_originalVolumeScalar, 0f, 1f) * 100);
                await _systemControlService.SetVolumePercentAsync(percent);
                await _systemControlService.SetMuteStateAsync(_originalMute);
            }
            catch
            {
                // 测试清理阶段不应导致额外失败：尽最大努力恢复即可。
            }
        }

        private static bool GetCurrentMute()
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return device.AudioEndpointVolume.Mute;
        }

        [Fact]
        public async Task SetVolumeAsync_IncreaseVolume_ShouldReturnSuccessMessage()
        {
            // Act
            var result = await _volumeTool.SetVolumeAsync(true);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);
            Assert.Contains("successful", result, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task SetVolumeAsync_DecreaseVolume_ShouldReturnSuccessMessage()
        {
            // Act
            var result = await _volumeTool.SetVolumeAsync(false);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);
            Assert.Contains("successful", result, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(25)]
        [InlineData(50)]
        [InlineData(75)]
        [InlineData(100)]
        public async Task SetVolumePercentAsync_WithValidPercentage_ShouldReturnSuccessMessage(int percent)
        {
            // Act
            var result = await _volumeTool.SetVolumePercentAsync(percent);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);
            Assert.Contains("successful", result, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData(-10)]
        [InlineData(150)]
        public async Task SetVolumePercentAsync_WithInvalidPercentage_ShouldHandleGracefully(int percent)
        {
            // Act
            var result = await _volumeTool.SetVolumePercentAsync(percent);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);
        }

        [Fact]
        public async Task GetCurrentVolumeAsync_ShouldReturnVolumeInfo()
        {
            // Act
            var result = await _volumeTool.GetCurrentVolumeAsync();

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);
            Assert.Contains("Current volume:", result);
            Assert.Contains("%", result);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task SetMuteStateAsync_WithValidState_ShouldReturnSuccessMessage(bool mute)
        {
            // Act
            var result = await _volumeTool.SetMuteStateAsync(mute);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);
            Assert.Contains("successful", result, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task VolumeOperations_Integration_ShouldWorkTogether()
        {
            // Arrange - Get initial volume
            var initialVolumeResult = await _volumeTool.GetCurrentVolumeAsync();
            Assert.NotNull(initialVolumeResult);

            // Act & Assert - Set specific volume
            var setResult = await _volumeTool.SetVolumePercentAsync(50);
            Assert.Contains("successful", setResult, StringComparison.OrdinalIgnoreCase);

            // Act & Assert - Mute
            var muteResult = await _volumeTool.SetMuteStateAsync(true);
            Assert.Contains("successful", muteResult, StringComparison.OrdinalIgnoreCase);

            // Act & Assert - Unmute
            var unmuteResult = await _volumeTool.SetMuteStateAsync(false);
            Assert.Contains("successful", unmuteResult, StringComparison.OrdinalIgnoreCase);

            // Act & Assert - Increase volume
            var increaseResult = await _volumeTool.SetVolumeAsync(true);
            Assert.Contains("successful", increaseResult, StringComparison.OrdinalIgnoreCase);

            // Act & Assert - Decrease volume
            var decreaseResult = await _volumeTool.SetVolumeAsync(false);
            Assert.Contains("successful", decreaseResult, StringComparison.OrdinalIgnoreCase);
        }
    }
}
