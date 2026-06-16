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
    /// DeleteDirectoryTool单元测试类
    /// </summary>
[Trait("Category", "FileSystem")]
public class DeleteDirectoryToolTest : IDisposable
    {
        private readonly IFileSystemService _fileSystemService;
        private readonly Mock<ILogger<DeleteDirectoryTool>> _mockLogger;
        private readonly string _baseDir;

        public DeleteDirectoryToolTest()
        {
            _baseDir = Path.Combine(Path.GetTempPath(), "Windows.Agent.Test", "FileSystem", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_baseDir);
            _fileSystemService = new FileSystemService(NullLogger<FileSystemService>.Instance);
            _mockLogger = new Mock<ILogger<DeleteDirectoryTool>>();
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
        public async Task DeleteDirectoryAsync_ShouldReturnSuccessMessage()
        {
            // Arrange
            var directoryPath = Path.Combine(_baseDir, "TempFolder");
            var recursive = false;
            
            // 创建要删除的目录
            Directory.CreateDirectory(directoryPath);
            
            var deleteDirectoryTool = new DeleteDirectoryTool(_fileSystemService, _mockLogger.Object);

            // Act
            var result = await deleteDirectoryTool.DeleteDirectoryAsync(directoryPath, recursive);

            // Assert
            var jsonResult = JsonSerializer.Deserialize<JsonElement>(result);
            Assert.True(jsonResult.GetProperty("success").GetBoolean());
            Assert.Equal(directoryPath, jsonResult.GetProperty("path").GetString());
            Assert.False(jsonResult.GetProperty("recursive").GetBoolean());
            
            // 验证目录是否确实被删除
            Assert.False(Directory.Exists(directoryPath));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task DeleteDirectoryAsync_WithRecursive_ShouldCallService(bool recursive)
        {
            // Arrange
            var directoryPath = Path.Combine(_baseDir, "TestFolder");
            
            // 创建要删除的目录结构
            Directory.CreateDirectory(directoryPath);
            if (recursive)
            {
                // 为递归测试创建子目录和文件
                var subDir = Path.Combine(directoryPath, "SubFolder");
                Directory.CreateDirectory(subDir);
                File.WriteAllText(Path.Combine(directoryPath, "test.txt"), "test content");
                File.WriteAllText(Path.Combine(subDir, "subtest.txt"), "sub test content");
            }
            
            var deleteDirectoryTool = new DeleteDirectoryTool(_fileSystemService, _mockLogger.Object);

            // Act
            var result = await deleteDirectoryTool.DeleteDirectoryAsync(directoryPath, recursive);

            // Assert
            var jsonResult = JsonSerializer.Deserialize<JsonElement>(result);
            Assert.True(jsonResult.GetProperty("success").GetBoolean());
            Assert.Equal(directoryPath, jsonResult.GetProperty("path").GetString());
            Assert.Equal(recursive, jsonResult.GetProperty("recursive").GetBoolean());
            
            // 验证目录是否确实被删除
            Assert.False(Directory.Exists(directoryPath));
        }

        [Fact]
        public async Task DeleteDirectoryAsync_WithNonRecursiveOnNonEmptyDirectory_ShouldFail()
        {
            // Arrange
            var directoryPath = Path.Combine(_baseDir, "NonEmptyFolder");
            
            // 创建非空目录
            Directory.CreateDirectory(directoryPath);
            File.WriteAllText(Path.Combine(directoryPath, "file.txt"), "content");
            
            var deleteDirectoryTool = new DeleteDirectoryTool(_fileSystemService, _mockLogger.Object);

            // Act
            var result = await deleteDirectoryTool.DeleteDirectoryAsync(directoryPath, false);

            // Assert
            var jsonResult = JsonSerializer.Deserialize<JsonElement>(result);
            Assert.False(jsonResult.GetProperty("success").GetBoolean());
            Assert.Equal(directoryPath, jsonResult.GetProperty("path").GetString());
            Assert.False(jsonResult.GetProperty("recursive").GetBoolean());
            
            // 验证目录仍然存在（因为非递归删除失败）
            Assert.True(Directory.Exists(directoryPath));
            
            // 清理测试目录
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, true);
            }
        }

        [Fact]
        public async Task DeleteDirectoryAsync_WithNonExistentDirectory_ShouldReturnError()
        {
            // Arrange
            var directoryPath = Path.Combine(_baseDir, "NonExistentFolder");
            var deleteDirectoryTool = new DeleteDirectoryTool(_fileSystemService, _mockLogger.Object);

            // Act
            var result = await deleteDirectoryTool.DeleteDirectoryAsync(directoryPath);

            // Assert
            var jsonResult = JsonSerializer.Deserialize<JsonElement>(result);
            Assert.False(jsonResult.GetProperty("success").GetBoolean());
            Assert.Equal(directoryPath, jsonResult.GetProperty("path").GetString());
        }

        [Fact]
        public async Task DeleteDirectoryAsync_WithInvalidPath_ShouldReturnError()
        {
            // Arrange
            var directoryPath = "Z:\\invalid\\path\\folder"; // 无效路径
            var deleteDirectoryTool = new DeleteDirectoryTool(_fileSystemService, _mockLogger.Object);

            // Act
            var result = await deleteDirectoryTool.DeleteDirectoryAsync(directoryPath);

            // Assert
            var jsonResult = JsonSerializer.Deserialize<JsonElement>(result);
            Assert.False(jsonResult.GetProperty("success").GetBoolean());
            Assert.Equal(directoryPath, jsonResult.GetProperty("path").GetString());
        }
    }
}
