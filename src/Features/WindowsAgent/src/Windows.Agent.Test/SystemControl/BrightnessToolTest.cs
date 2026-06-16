using Windows.Agent.Interface;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Windows.Agent.Tools.SystemControl;
using Windows.Agent.Services;
using Xunit;

namespace Windows.Agent.Test.SystemControl
{
    /// <summary>
    /// BrightnessTool单元测试类
    /// </summary>
[Trait("Category", "SystemControl")]
    [Collection("SystemControl")]
    public class BrightnessToolTest : IAsyncLifetime
    {
        private readonly ISystemControlService _systemControlService;
        private readonly ILogger<BrightnessTool> _logger;
        private readonly BrightnessTool _brightnessTool;

        private bool _captured;
        private byte _originalBrightness;

        public BrightnessToolTest()
        {
            // 创建服务容器并注册依赖
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole());
            services.AddSingleton<ISystemControlService, SystemControlService>();
            
            var serviceProvider = services.BuildServiceProvider();
            
            // 获取实际的服务实例
            _systemControlService = serviceProvider.GetRequiredService<ISystemControlService>();
            _logger = serviceProvider.GetRequiredService<ILogger<BrightnessTool>>();
            _brightnessTool = new BrightnessTool(_systemControlService, _logger);
        }

        public async Task InitializeAsync()
        {
            _originalBrightness = await _systemControlService.GetCurrentBrightnessAsync();
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
                // 当前实现对亮度有最小值限制（见 SystemControlService），恢复时按实现限制尽量还原。
                var target = Math.Max(_originalBrightness, (byte)20);
                await _systemControlService.SetBrightnessPercentAsync(target);
            }
            catch
            {
                // 清理阶段不额外失败
            }
        }

        [Fact]
        public async Task SetBrightnessAsync_IncreaseBrightness_ShouldReturnSuccessMessage()
        {
            // Act
            var result = await _brightnessTool.SetBrightnessAsync(true);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);
            Assert.Contains("successful", result, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task SetBrightnessAsync_DecreaseBrightness_ShouldReturnSuccessMessage()
        {
            // Act
            var result = await _brightnessTool.SetBrightnessAsync(false);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);
            Assert.Contains("successful", result, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData(20)]  // Minimum brightness
        [InlineData(40)]
        [InlineData(60)]
        [InlineData(80)]
        [InlineData(100)] // Maximum brightness
        public async Task SetBrightnessPercentAsync_WithValidPercentage_ShouldReturnSuccessMessage(int percent)
        {
            // Act
            var result = await _brightnessTool.SetBrightnessPercentAsync(percent);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);
            Assert.Contains("successful", result, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData(0)]   // Below minimum, should be clamped to 20
        [InlineData(10)]  // Below minimum, should be clamped to 20
        [InlineData(150)] // Above maximum, should be handled gracefully
        public async Task SetBrightnessPercentAsync_WithEdgeCases_ShouldHandleGracefully(int percent)
        {
            // Act
            var result = await _brightnessTool.SetBrightnessPercentAsync(percent);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);
        }

        [Fact]
        public async Task GetCurrentBrightnessAsync_ShouldReturnBrightnessInfo()
        {
            // Act
            var result = await _brightnessTool.GetCurrentBrightnessAsync();

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);
            Assert.Contains("Current brightness:", result);
            Assert.Contains("%", result);
        }

        [Fact]
        public async Task BrightnessOperations_Integration_ShouldWorkTogether()
        {
            // Arrange - Get initial brightness
            var initialBrightnessResult = await _brightnessTool.GetCurrentBrightnessAsync();
            Assert.NotNull(initialBrightnessResult);

            // Act & Assert - Set specific brightness
            var setResult = await _brightnessTool.SetBrightnessPercentAsync(60);
            Assert.Contains("successful", setResult, StringComparison.OrdinalIgnoreCase);

            // Act & Assert - Increase brightness
            var increaseResult = await _brightnessTool.SetBrightnessAsync(true);
            Assert.Contains("successful", increaseResult, StringComparison.OrdinalIgnoreCase);

            // Act & Assert - Decrease brightness
            var decreaseResult = await _brightnessTool.SetBrightnessAsync(false);
            Assert.Contains("successful", decreaseResult, StringComparison.OrdinalIgnoreCase);

            // Act & Assert - Set to minimum brightness
            var minResult = await _brightnessTool.SetBrightnessPercentAsync(20);
            Assert.Contains("successful", minResult, StringComparison.OrdinalIgnoreCase);

            // Act & Assert - Set to maximum brightness
            var maxResult = await _brightnessTool.SetBrightnessPercentAsync(100);
            Assert.Contains("successful", maxResult, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task BrightnessAdjustment_MultipleOperations_ShouldMaintainConsistency()
        {
            // Test multiple brightness adjustments in sequence
            for (int i = 0; i < 3; i++)
            {
                var increaseResult = await _brightnessTool.SetBrightnessAsync(true);
                Assert.Contains("successful", increaseResult, StringComparison.OrdinalIgnoreCase);

                var decreaseResult = await _brightnessTool.SetBrightnessAsync(false);
                Assert.Contains("successful", decreaseResult, StringComparison.OrdinalIgnoreCase);
            }

            // Verify we can still get current brightness
            var finalBrightnessResult = await _brightnessTool.GetCurrentBrightnessAsync();
            Assert.NotNull(finalBrightnessResult);
            Assert.Contains("Current brightness:", finalBrightnessResult);
        }
    }
}
