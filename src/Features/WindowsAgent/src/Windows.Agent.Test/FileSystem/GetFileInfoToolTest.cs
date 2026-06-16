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
    /// GetFileInfoTool单元测试类
    /// </summary>
[Trait("Category", "FileSystem")]
public class GetFileInfoToolTest : IDisposable
    {
        private readonly IFileSystemService _fileSystemService;
        private readonly Mock<ILogger<GetFileInfoTool>> _mockLogger;
        private readonly string _baseDir;

        public GetFileInfoToolTest()
        {
            _baseDir = Path.Combine(Path.GetTempPath(), "Windows.Agent.Test", "FileSystem", nameof(GetFileInfoToolTest), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_baseDir);
            _fileSystemService = new FileSystemService(NullLogger<FileSystemService>.Instance);
            _mockLogger = new Mock<ILogger<GetFileInfoTool>>();
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
        public async Task GetFileInfoAsync_ShouldReturnFileInfo()
        {
            // Arrange
            var filePath = Path.Combine(_baseDir, "test.txt");
            var testContent = "This is test content for file info";
            
            // 创建测试文件
            File.WriteAllText(filePath, testContent);
            var getFileInfoTool = new GetFileInfoTool(_fileSystemService, _mockLogger.Object);

            // Act
            var result = await getFileInfoTool.GetFileInfoAsync(filePath);

            // Assert
            var jsonResult = JsonSerializer.Deserialize<JsonElement>(result);
            Assert.True(jsonResult.GetProperty("success").GetBoolean());
            Assert.Equal(filePath, jsonResult.GetProperty("path").GetString());
            Assert.True(jsonResult.GetProperty("fileExists").GetBoolean());
            
            // 验证文件信息包含基本属性
            var info = jsonResult.GetProperty("info").GetString();
            Assert.Contains("test.txt", info);
            Assert.Contains(testContent.Length.ToString(), info);
            
            // 清理测试文件
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        [Theory]
        [InlineData("document.pdf")]
        [InlineData("photo.jpg")]
        [InlineData("config.json")]
        public async Task GetFileInfoAsync_WithDifferentFiles_ShouldCallService(string fileName)
        {
            // Arrange
            var filePath = Path.Combine(_baseDir, fileName);
            var testContent = $"Test content for {fileName}";
            
            // 创建测试文件
            File.WriteAllText(filePath, testContent);
            var getFileInfoTool = new GetFileInfoTool(_fileSystemService, _mockLogger.Object);

            // Act
            var result = await getFileInfoTool.GetFileInfoAsync(filePath);

            // Assert
            var jsonResult = JsonSerializer.Deserialize<JsonElement>(result);
            Assert.True(jsonResult.GetProperty("success").GetBoolean());
            Assert.Equal(filePath, jsonResult.GetProperty("path").GetString());
            Assert.True(jsonResult.GetProperty("fileExists").GetBoolean());
            
            // 验证文件信息包含文件名和大小
            var info = jsonResult.GetProperty("info").GetString();
            Assert.Contains(fileName, info);
            Assert.Contains(testContent.Length.ToString(), info);
            
            // 清理测试文件
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        [Fact]
        public async Task GetFileInfoAsync_ForDirectory_ShouldReturnDirectoryInfo()
        {
            // Arrange
            var directoryPath = Path.Combine(_baseDir, "testdir");
            
            // 确保基础目录存在
            Directory.CreateDirectory(directoryPath);
            
            var getFileInfoTool = new GetFileInfoTool(_fileSystemService, _mockLogger.Object);

            // Act
            var result = await getFileInfoTool.GetFileInfoAsync(directoryPath);

            // Assert
            var jsonResult = JsonSerializer.Deserialize<JsonElement>(result);
            Assert.True(jsonResult.GetProperty("success").GetBoolean());
            Assert.Equal(directoryPath, jsonResult.GetProperty("path").GetString());
            Assert.False(jsonResult.GetProperty("fileExists").GetBoolean());
            Assert.True(jsonResult.GetProperty("directoryExists").GetBoolean());
            
            // 验证目录信息
            var info = jsonResult.GetProperty("info").GetString();
            Assert.Contains("testdir", info);
            
            // 清理测试目录
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, true);
            }
        }

        [Fact]
        public async Task GetFileInfoAsync_WithNonExistentPath_ShouldReturnNotFound()
        {
            // Arrange
            var filePath = Path.Combine(_baseDir, "nonexistent.txt");
            var getFileInfoTool = new GetFileInfoTool(_fileSystemService, _mockLogger.Object);

            // Act
            var result = await getFileInfoTool.GetFileInfoAsync(filePath);

            // Assert
            var jsonResult = JsonSerializer.Deserialize<JsonElement>(result);
            Assert.False(jsonResult.GetProperty("success").GetBoolean());
            Assert.Equal(filePath, jsonResult.GetProperty("path").GetString());
            Assert.False(jsonResult.GetProperty("fileExists").GetBoolean());
            Assert.False(jsonResult.GetProperty("directoryExists").GetBoolean());
        }

        [Fact]
        public async Task GetFileInfoAsync_WithInvalidPath_ShouldReturnError()
        {
            // Arrange
            var filePath = "Z:\\invalid\\path\\file.txt"; // 无效路径
            var getFileInfoTool = new GetFileInfoTool(_fileSystemService, _mockLogger.Object);

            // Act
            var result = await getFileInfoTool.GetFileInfoAsync(filePath);

            // Assert
            var jsonResult = JsonSerializer.Deserialize<JsonElement>(result);
            Assert.False(jsonResult.GetProperty("success").GetBoolean());
            Assert.Equal(filePath, jsonResult.GetProperty("path").GetString());
            Assert.False(jsonResult.GetProperty("fileExists").GetBoolean());
            Assert.False(jsonResult.GetProperty("directoryExists").GetBoolean());
        }

        [Fact]
        public async Task GetFileInfoAsync_WithEmptyFile_ShouldReturnInfo()
        {
            // Arrange
            var filePath = Path.Combine(_baseDir, "empty.txt");
            
            // 创建空文件
            File.WriteAllText(filePath, "");
            var getFileInfoTool = new GetFileInfoTool(_fileSystemService, _mockLogger.Object);

            // Act
            var result = await getFileInfoTool.GetFileInfoAsync(filePath);

            // Assert
            var jsonResult = JsonSerializer.Deserialize<JsonElement>(result);
            Assert.True(jsonResult.GetProperty("success").GetBoolean());
            Assert.Equal(filePath, jsonResult.GetProperty("path").GetString());
            Assert.True(jsonResult.GetProperty("fileExists").GetBoolean());
            
            // 验证文件信息显示大小为0
            var info = jsonResult.GetProperty("info").GetString();
            Assert.Contains("empty.txt", info);
            Assert.Contains("0", info);
            
            // 清理测试文件
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }
}
