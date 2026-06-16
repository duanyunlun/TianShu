using Windows.Agent.Interface;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Windows.Agent.Tools.FileSystem;
using Windows.Agent.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Windows.Agent.Test.FileSystem
{
    /// <summary>
    /// CopyFileTool单元测试类
    /// </summary>
[Trait("Category", "FileSystem")]
public class CopyFileToolTest : IDisposable
    {
        private readonly IFileSystemService _fileSystemService;
        private readonly Mock<ILogger<CopyFileTool>> _mockLogger;
        private readonly string _baseDir;

        public CopyFileToolTest()
        {
            _baseDir = Path.Combine(Path.GetTempPath(), "Windows.Agent.Test", "FileSystem", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_baseDir);
            _fileSystemService = new FileSystemService(NullLogger<FileSystemService>.Instance);
            _mockLogger = new Mock<ILogger<CopyFileTool>>();
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_baseDir))
                {
                    Directory.Delete(_baseDir, true);
                }
            }
            catch
            {
                // 清理阶段不额外失败
            }
        }

        [Fact]
        public async Task CopyFileAsync_ShouldReturnSuccessMessage()
        {
            // Arrange
            var source = Path.Combine(_baseDir, "source.txt");
            var destination = Path.Combine(_baseDir, "destination.txt");
            // 创建源文件用于测试
            if (!File.Exists(source))
            {
                File.WriteAllText(source, "Test content");
            }
            // 确保目标文件不存在
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }
            
            var copyFileTool = new CopyFileTool(_fileSystemService, _mockLogger.Object);

            // Act
            var result = await copyFileTool.CopyFileAsync(source, destination);

            // Assert
            var jsonResult = JsonSerializer.Deserialize<JsonElement>(result);
            Assert.True(jsonResult.GetProperty("success").GetBoolean());
            Assert.Equal(source, jsonResult.GetProperty("source").GetString());
            Assert.Equal(destination, jsonResult.GetProperty("destination").GetString());
            Assert.False(jsonResult.GetProperty("overwrite").GetBoolean());
            // 验证文件是否确实被复制
            Assert.True(File.Exists(destination));
            
            // 清理测试文件
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task CopyFileAsync_WithOverwrite_ShouldCallService(bool overwrite)
        {
            // Arrange
            var source = Path.Combine(_baseDir, "test.txt");
            var destination = Path.Combine(_baseDir, "copy.txt");
            // 创建源文件
            File.WriteAllText(source, "Test content for overwrite test");
            
            if (overwrite)
            {
                // 如果测试覆盖，先创建目标文件
                File.WriteAllText(destination, "Original content");
            }
            else
            {
                // 确保目标文件不存在
                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }
            }
            
            var copyFileTool = new CopyFileTool(_fileSystemService, _mockLogger.Object);

            // Act
            var result = await copyFileTool.CopyFileAsync(source, destination, overwrite);

            // Assert
            var jsonResult = JsonSerializer.Deserialize<JsonElement>(result);
            Assert.True(jsonResult.GetProperty("success").GetBoolean());
            Assert.Equal(source, jsonResult.GetProperty("source").GetString());
            Assert.Equal(destination, jsonResult.GetProperty("destination").GetString());
            Assert.Equal(overwrite, jsonResult.GetProperty("overwrite").GetBoolean());
            // 验证文件是否确实被复制
            Assert.True(File.Exists(destination));
            
            // 清理测试文件
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }
            if (File.Exists(source))
            {
                File.Delete(source);
            }
        }

        [Fact]
        public async Task CopyFileAsync_WithNonExistentSource_ShouldReturnError()
        {
            // Arrange
            var source = Path.Combine(_baseDir, "nonexistent.txt");
            var destination = Path.Combine(_baseDir, "dest.txt");
            var copyFileTool = new CopyFileTool(_fileSystemService, _mockLogger.Object);

            // Act
            var result = await copyFileTool.CopyFileAsync(source, destination);

            // Assert
            var jsonResult = JsonSerializer.Deserialize<JsonElement>(result);
            Assert.False(jsonResult.GetProperty("success").GetBoolean());
            Assert.Equal(source, jsonResult.GetProperty("source").GetString());
            Assert.Equal(destination, jsonResult.GetProperty("destination").GetString());
        }

        [Fact]
        public async Task CopyFileAsync_WithInvalidPath_ShouldReturnError()
        {
            // Arrange
            var source = Path.Combine(_baseDir, "test.txt");
            var destination = "Z:\\invalid\\path\\dest.txt"; // 无效路径
            // 创建源文件
            File.WriteAllText(source, "Test content");
            
            var copyFileTool = new CopyFileTool(_fileSystemService, _mockLogger.Object);

            // Act
            var result = await copyFileTool.CopyFileAsync(source, destination);

            // Assert
            var jsonResult = JsonSerializer.Deserialize<JsonElement>(result);
            Assert.False(jsonResult.GetProperty("success").GetBoolean());
            Assert.Equal(source, jsonResult.GetProperty("source").GetString());
            Assert.Equal(destination, jsonResult.GetProperty("destination").GetString());
            
            // 清理测试文件
            if (File.Exists(source))
            {
                File.Delete(source);
            }
        }
    }
}
