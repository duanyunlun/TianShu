# Windows Agent.NET

**English** | [中文](README.md)

A .NET-based Windows desktop automation **CLI toolkit** for LLMs to call via shell tools. (This repository no longer provides an MCP server.)

## 📋 Table of Contents

- [Features](#-features)
- [Use Cases](#-use-cases)
- [Demo Screenshots](#-demo-screenshots)
- [Tech Stack](#️-tech-stack)
- [Quick Start](#-quick-start)
- [API Documentation](#-api-documentation)
- [Project Structure](#️-project-structure)
- [Feature Extension Suggestions](#-feature-extension-suggestions)
- [Configuration](#-configuration)
- [Contributing](#-contributing)
- [Changelog](#-changelog)
- [Support](#-support)

## 🚀 Quick Start

### Prerequisites
- Windows Operating System
- .NET 10.0 Runtime or higher

**Important Note**: This project requires .NET 10 to run. Please ensure you have .NET 10 installed locally. If not installed, please visit the [.NET 10 Download Page](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) to download and install.

### 1. Installation and Running

#### Method 1: Global Installation (Recommended)
```bash
dotnet tool install --global Windows.Agent.Cli

# Help
windows-agent help
```

#### Method 2: Run from Source
```bash
# Clone repository
git clone https://github.com/duanyunlun/Windows-Agent.NET.git
cd Windows-Agent.NET

# Build project
dotnet build

# Run CLI (dev mode)
dotnet run --project src/Windows.Agent.Cli/Windows.Agent.Cli.csproj -- help
```

### 2. CLI Mode (Examples)
CLI outputs JSON to stdout by default:

```bash
# Help
dotnet run --project src/Windows.Agent.Cli/Windows.Agent.Cli.csproj -- help

# Desktop state (no desktop interaction)
dotnet run --project src/Windows.Agent.Cli/Windows.Agent.Cli.csproj -- desktop state --pretty

# Mouse click
dotnet run --project src/Windows.Agent.Cli/Windows.Agent.Cli.csproj -- desktop click --x 100 --y 200 --button left --clicks 1

# Read a file
dotnet run --project src/Windows.Agent.Cli/Windows.Agent.Cli.csproj -- fs read --path \"./temp/a.txt\"
```

> Note: The CLI calls existing `Windows.Agent.Tools.*` classes (instead of calling Services directly) to reuse tool-level behavior and parameters.

## 🚀 Features

### Core Functionality
- **Application Launch**: Launch applications from the Start Menu by name
- **PowerShell Integration**: Execute PowerShell commands and return results
- **Desktop State Capture**: Capture current desktop state including active applications, UI elements, etc.
- **Clipboard Operations**: Copy and paste text content
- **Mouse Operations**: Click, drag, move mouse cursor
- **Keyboard Operations**: Text input, key presses, keyboard shortcuts
- **Window Management**: Resize windows, adjust positions, switch applications
- **Scroll Operations**: Scroll at specified coordinates
- **Web Scraping**: Fetch web content and convert to Markdown format
- **Browser Operations**: Open specified URLs in default browser
- **Screenshot Functionality**: Capture screen and save to temporary directory
- **File System Operations**: Create, read, write, copy, move, delete files and directories
- **OCR Text Recognition**: Extract text from screen or specified regions, find text locations
- **System Control**: Adjust screen brightness, system volume, screen resolution and other system settings
- **Wait Control**: Add delays between operations

### Supported Tools

## Desktop Operation Tools

| Tool Name | Description |
|-----------|-------------|
| **LaunchTool** | Launch applications from the Start Menu |
| **PowershellTool** | Execute PowerShell commands and return status codes |
| **StateTool** | Capture desktop state information including applications and UI elements |
| **ClipboardTool** | Clipboard copy and paste operations |
| **ClickTool** | Mouse click operations (supports left, right, middle buttons, single, double, triple clicks) |
| **TypeTool** | Input text at specified coordinates with clear and enter support |
| **ResizeTool** | Resize window size and position |
| **SwitchTool** | Switch to specified application window |
| **ScrollTool** | Scroll at specified coordinates or current mouse position |
| **DragTool** | Drag from source coordinates to target coordinates |
| **MoveTool** | Move mouse cursor to specified coordinates |
| **ShortcutTool** | Execute keyboard shortcut combinations |
| **KeyTool** | Press individual keyboard keys |
| **WaitTool** | Pause execution for specified seconds |
| **ScrapeTool** | Scrape web content and convert to Markdown format |
| **ScreenshotTool** | Capture screen and save to temporary directory, return image path |
| **OpenBrowserTool** | Open specified URL in default browser |

## FileSystem Tools

| Tool Name | Description |
|-----------|-------------|
| **ReadFileTool** | Read content from specified file |
| **WriteFileTool** | Write content to file |
| **CreateFileTool** | Create new file with specified content |
| **CopyFileTool** | Copy file to specified location |
| **MoveFileTool** | Move or rename file |
| **DeleteFileTool** | Delete specified file |
| **GetFileInfoTool** | Get file information (size, creation time, etc.) |
| **ListDirectoryTool** | List files and subdirectories in directory |
| **CreateDirectoryTool** | Create new directory |
| **DeleteDirectoryTool** | Delete directory and its contents |
| **SearchFilesTool** | Search for files in specified directory |

## OCR Image Recognition Tools

| Tool Name | Description |
|-----------|-------------|
| **ExtractTextFromScreenTool** | Extract text from entire screen using OCR |
| **ExtractTextFromRegionTool** | Extract text from specified screen region using OCR |
| **FindTextOnScreenTool** | Find specified text on screen using OCR |
| **GetTextCoordinatesTool** | Get coordinates of text on screen |
| **ExtractTextFromFileTool** | Extract text from image files using OCR |

## UI Element Recognition Tools

| Tool Name | Description |
|-----------|-------------|
| **FindElementByTextTool** | Find UI elements by text content |
| **FindElementByClassNameTool** | Find UI elements by class name |
| **FindElementByAutomationIdTool** | Find UI elements by automation ID |
| **GetElementPropertiesTool** | Get properties of element at specified coordinates |
| **WaitForElementTool** | Wait for specified element to appear on screen |

## SystemControl Tools

| Tool Name | Description |
|-----------|-------------|
| **BrightnessTool** | Adjust screen brightness, supports increase/decrease and specific percentage |
| **VolumeTool** | Adjust system volume, supports increase/decrease and specific percentage |
| **ResolutionTool** | Set screen resolution (high, medium, low settings) |

## 💡 Use Cases

### 🤖 AI Assistant Desktop Automation
- **Intelligent Customer Service Robot**: AI assistants can automatically operate Windows applications to help users complete complex desktop tasks
- **Voice Assistant Integration**: Combined with voice recognition, control desktop applications through voice commands
- **Intelligent Office Assistant**: AI assistants automatically handle daily office tasks such as document organization, email sending, etc.

### 📊 Office Automation
- **Data Entry Automation**: Automatically extract data from web pages or documents and enter it into Excel or other applications
- **Report Generation**: Automatically collect system information, screenshots, and generate formatted report documents
- **Batch File Processing**: Automatically organize, rename, and categorize large numbers of files and documents
- **Email Automation**: Automatically send periodic reports and notification emails

### 🧪 Software Testing & Quality Assurance
- **UI Automation Testing**: Simulate user operations to automatically test desktop application functionality
- **Regression Testing**: Automatically execute repetitive test cases to ensure software quality
- **Performance Monitoring**: Automatically collect application performance data and generate monitoring reports
- **Bug Reproduction**: Automatically reproduce user-reported issues to assist developers in debugging

### 🎯 Business Process Automation
- **Customer Service**: Automatically handle customer requests and update CRM systems
- **Order Processing**: Automatically collect order information from multiple channels and enter it into systems
- **Inventory Management**: Automatically update inventory data and generate restocking reminders
- **Financial Reconciliation**: Automatically compare financial data from different systems and mark discrepancies

### 🔍 Data Collection & Analysis
- **Web Data Scraping**: Automatically collect product prices, news, and other information from multiple websites
- **Competitive Analysis**: Regularly collect competitor product information and pricing data
- **Market Research**: Automatically collect and organize market data, generate analysis reports
- **Social Media Monitoring**: Monitor brand mentions and automatically collect user feedback

### 🎮 Gaming & Entertainment
- **Game Assistance**: Automatically execute repetitive game tasks (please follow game rules)
- **Streaming Assistant**: Automatically manage streaming software, switch scenes, send messages
- **Media Management**: Automatically organize music and video files, update media libraries

### 🏥 Healthcare & Medical
- **Medical Record Entry**: Automatically convert paper medical records to electronic format
- **Medical Image Analysis**: Combined with OCR technology, automatically extract key information from medical reports
- **Appointment Management**: Automatically handle patient appointment requests and update hospital management systems

### 🏫 Education & Training
- **Online Examinations**: Automatically grade multiple-choice questions and generate grade reports
- **Course Management**: Automatically update course information and send notifications to students
- **Learning Progress Tracking**: Automatically record student learning activities and generate progress reports

### 🏭 Manufacturing & Logistics
- **Production Data Collection**: Automatically collect data from production equipment and update ERP systems
- **Quality Inspection**: Combined with image recognition, automatically detect product quality
- **Logistics Tracking**: Automatically update cargo status and send tracking information to customers

### 🔧 System Operations
- **Server Monitoring**: Automatically check server status and generate monitoring reports
- **Log Analysis**: Automatically analyze system logs and identify abnormal patterns
- **Backup Management**: Automatically execute data backups and verify backup integrity
- **Software Deployment**: Automate software installation and configuration processes

## 📸 Demo Screenshots

### Text Input Demo
Automatic text input in Notepad using TypeTool:

![Text Input Demo](assets/NotepadWriting.png)

### Web Search Demo
Open and search web content using ScrapeTool:

![Web Search Demo](assets/OpenWebSearch.png)

### 📹 Demo Video
Complete desktop automation operation demo:

[网页搜索演示](assets/video.mp4)

## 📸 Demo Screenshots

### Text Input Demo
Automatic text input in Notepad using TypeTool:

![Text Input Demo](assets/NotepadWriting.png)

### Web Search Demo
Using ScrapeTool to open and search web content:

![Web Search Demo](assets/OpenWebSearch.png)

### 📹 Demo Video
Complete desktop automation operation demonstration:

[Web Search Demo](assets/video.mp4)

## 🛠️ Tech Stack

- **.NET 10.0**: Based on the latest .NET framework
- **Microsoft.Extensions.Hosting**: Application hosting framework
- **HtmlAgilityPack**: HTML parsing and web scraping
- **ReverseMarkdown**: HTML to Markdown conversion

## 🚧 Feature Extension Suggestions

### Planned Features

#### Advanced UI Recognition & Interaction
- **Enhanced UI Element Recognition**: Support for more UI frameworks (WPF, WinForms, UWP)
- **OCR Text Recognition Optimization**: Multi-language support, improved recognition accuracy
- **Smart Wait Mechanism**: Dynamic waiting for element loading completion

#### Enhanced File System Operations
- **Advanced File Search**: Support for content search, regular expression matching
- **Batch File Operations**: Support for batch copy, move, rename
- **File Monitoring**: Real-time file system change monitoring

#### System Monitoring & Performance Analysis
- **System Resource Monitoring**: CPU, memory, disk, network usage
- **Process Management**: Process listing, performance monitoring, process control
- **Performance Analysis Reports**: Generate detailed system performance reports

#### Multimedia Processing Capabilities
- **Audio Control**: System volume control, audio device management
- **Image Processing**: Image scaling, cropping, format conversion
- **Screen Recording**: Support for screen recording and playback

#### Network & Communication Features
- **Network Diagnostics**: Ping, port scanning, connectivity testing
- **HTTP Client**: Support for RESTful API calls
- **WiFi Management**: WiFi network scanning and connection management

#### Security & Permission Management
- **Permission Checking**: User permission verification and management
- **Data Encryption**: Encrypted storage of sensitive data
- **Operation Auditing**: Complete operation logs and audit trails

### Development Roadmap

#### Phase 1 (High Priority) - Core Feature Enhancement
- ✅ UI Element Recognition Tools (Completed Windows API implementation)
- 🔄 Enhanced File Management Tools
- 📋 System Monitoring Tools
- 🔒 Basic Security Tools

#### Phase 2 (Medium Priority) - Feature Expansion
- 📋 OCR Text Recognition Optimization
- 📋 Advanced File Search
- 📋 Audio Control Tools
- 📋 Network Diagnostic Tools
- 📋 Excel Operation Support

#### Phase 3 (Low Priority) - Advanced Features
- 📋 Image Processing Tools
- 📋 Task Scheduling System
- 📋 Database Operation Support
- 📋 Macro Recording & Playback

## 🏗️ Project Structure

```
src/
├── Windows.Agent.Cli/         # CLI entry (public entrypoint)
├── Windows.Agent.Cli.Test/    # CLI dispatcher unit tests (mock, no desktop side effects)
├── Windows.Agent/         # Capability library (Services + Tools)
│   ├── Exceptions/          # Custom exception classes (to be extended)
│   ├── Interface/           # Service interface definitions
│   │   ├── IDesktopService.cs   # Desktop service interface
│   │   ├── IFileSystemService.cs # File system service interface
│   │   └── IOcrService.cs       # OCR service interface
│   ├── Models/              # Data models (to be extended)
│   ├── Prompts/             # Prompt templates (to be extended)
│   ├── Services/            # Core service implementations
│   │   ├── DesktopService.cs    # Desktop operation service
│   │   ├── FileSystemService.cs # File system service
│   │   └── OcrService.cs        # OCR service
│   ├── Tools/               # Tools (called by CLI)
│   │   ├── Desktop/             # Desktop operation tools
│   │   │   ├── ClickTool.cs         # Click tool
│   │   │   ├── ClipboardTool.cs     # Clipboard tool
│   │   │   ├── DragTool.cs          # Drag tool
│   │   │   ├── GetWindowInfoTool.cs # Window info tool
│   │   │   ├── KeyTool.cs           # Key tool
│   │   │   ├── LaunchTool.cs        # App launch tool
│   │   │   ├── MoveTool.cs          # Mouse move tool
│   │   │   ├── OpenBrowserTool.cs   # Browser open tool
│   │   │   ├── PowershellTool.cs    # PowerShell execution tool
│   │   │   ├── ResizeTool.cs        # Window resize tool
│   │   │   ├── ScrapeTool.cs        # Web scraping tool
│   │   │   ├── ScreenshotTool.cs    # Screenshot tool
│   │   │   ├── ScrollTool.cs        # Scroll tool
│   │   │   ├── ShortcutTool.cs      # Shortcut tool
│   │   │   ├── StateTool.cs         # Desktop state tool
│   │   │   ├── SwitchTool.cs        # App switch tool
│   │   │   ├── TypeTool.cs          # Text input tool
│   │   │   ├── UIElementTool.cs     # UI element operation tool
│   │   │   └── WaitTool.cs          # Wait tool
│   │   ├── FileSystem/          # File system tools
│   │   │   ├── CopyFileTool.cs      # File copy tool
│   │   │   ├── CreateDirectoryTool.cs # Directory creation tool
│   │   │   ├── CreateFileTool.cs    # File creation tool
│   │   │   ├── DeleteDirectoryTool.cs # Directory deletion tool
│   │   │   ├── DeleteFileTool.cs    # File deletion tool
│   │   │   ├── GetFileInfoTool.cs   # File info tool
│   │   │   ├── ListDirectoryTool.cs # Directory listing tool
│   │   │   ├── MoveFileTool.cs      # File move tool
│   │   │   ├── ReadFileTool.cs      # File read tool
│   │   │   ├── SearchFilesTool.cs   # File search tool
│   │   │   └── WriteFileTool.cs     # File write tool
│   │   └── OCR/                 # OCR recognition tools
│   │       ├── ExtractTextFromRegionTool.cs # Region text extraction tool
│   │       ├── ExtractTextFromScreenTool.cs # Screen text extraction tool
│   │       ├── FindTextOnScreenTool.cs      # Screen text search tool
│   │       └── GetTextCoordinatesTool.cs    # Text coordinate tool
│   └── Windows.Agent.csproj   # Project file
└── Windows.Agent.Test/    # Test project
    ├── DesktopToolsExtendedTest.cs  # Desktop tools extended test
    ├── FileSystemToolsExtendedTest.cs # File system tools extended test
    ├── OCRToolsExtendedTest.cs      # OCR tools extended test
    ├── ToolTest.cs                  # Tool basic test
    ├── UIElementToolTest.cs         # UI element tool test
    └── Windows.Agent.Test.csproj  # Test project file
```

## 📦 Installation

### Prerequisites
- Windows Operating System
- .NET 10.0 Runtime or higher

**Important Note**: This project requires .NET 10 to run. Please ensure you have .NET 10 installed locally. If not installed, please visit the [.NET 10 Download Page](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) to download and install.

### Build from Source

```bash
# Clone repository
git clone https://github.com/duanyunlun/Windows-Agent.NET.git
cd Windows-Agent.NET

# Build project
dotnet build

# Run CLI (dev mode)
dotnet run --project src/Windows.Agent.Cli/Windows.Agent.Cli.csproj -- help
```

### NuGet Package Installation

```bash
dotnet tool install --global Windows.Agent.Cli
```

## 🚀 Usage
### CLI (installed)

```bash
windows-agent help
windows-agent desktop state --pretty
```

### CLI (dev mode)

```bash
dotnet run --project src/Windows.Agent.Cli/Windows.Agent.Cli.csproj -- help
```


## 🏗️ Project Structure

```
src/
├── Windows.Agent.Cli/         # CLI entry (public entrypoint)
├── Windows.Agent.Cli.Test/    # CLI dispatcher unit tests (mock, no desktop side effects)
├── Windows.Agent/         # Capability library (Services + Tools)
│   ├── Exceptions/          # Custom exception classes (to be extended)
│   ├── Interface/           # Service interface definitions
│   │   ├── IDesktopService.cs   # Desktop service interface
│   │   ├── IFileSystemService.cs # File system service interface
│   │   └── IOcrService.cs       # OCR service interface
│   ├── Models/              # Data models (to be extended)
│   ├── Prompts/             # Prompt templates (to be extended)
│   ├── Services/            # Core service implementations
│   │   ├── DesktopService.cs    # Desktop operation service
│   │   ├── FileSystemService.cs # File system service
│   │   └── OcrService.cs        # OCR service
│   ├── Tools/               # Tools (called by CLI)
│   │   ├── Desktop/             # Desktop operation tools
│   │   │   ├── ClickTool.cs         # Click tool
│   │   │   ├── ClipboardTool.cs     # Clipboard tool
│   │   │   ├── DragTool.cs          # Drag tool
│   │   │   ├── GetWindowInfoTool.cs # Window info tool
│   │   │   ├── KeyTool.cs           # Key press tool
│   │   │   ├── LaunchTool.cs        # Application launch tool
│   │   │   ├── MoveTool.cs          # Mouse move tool
│   │   │   ├── OpenBrowserTool.cs   # Browser open tool
│   │   │   ├── PowershellTool.cs    # PowerShell execution tool
│   │   │   ├── ResizeTool.cs        # Window resize tool
│   │   │   ├── ScrapeTool.cs        # Web scraping tool
│   │   │   ├── ScreenshotTool.cs    # Screenshot tool
│   │   │   ├── ScrollTool.cs        # Scroll tool
│   │   │   ├── ShortcutTool.cs      # Keyboard shortcut tool
│   │   │   ├── StateTool.cs         # Desktop state tool
│   │   │   ├── SwitchTool.cs        # Application switch tool
│   │   │   ├── TypeTool.cs          # Text input tool
│   │   │   ├── UIElementTool.cs     # UI element operation tool
│   │   │   └── WaitTool.cs          # Wait tool
│   │   ├── FileSystem/          # File system tools
│   │   │   ├── CopyFileTool.cs      # File copy tool
│   │   │   ├── CreateDirectoryTool.cs # Directory creation tool
│   │   │   ├── CreateFileTool.cs    # File creation tool
│   │   │   ├── DeleteDirectoryTool.cs # Directory deletion tool
│   │   │   ├── DeleteFileTool.cs    # File deletion tool
│   │   │   ├── GetFileInfoTool.cs   # File info tool
│   │   │   ├── ListDirectoryTool.cs # Directory listing tool
│   │   │   ├── MoveFileTool.cs      # File move tool
│   │   │   ├── ReadFileTool.cs      # File read tool
│   │   │   ├── SearchFilesTool.cs   # File search tool
│   │   │   └── WriteFileTool.cs     # File write tool
│   │   └── OCR/                 # OCR recognition tools
│   │       ├── ExtractTextFromRegionTool.cs # Region text extraction tool
│   │       ├── ExtractTextFromScreenTool.cs # Screen text extraction tool
│   │       ├── FindTextOnScreenTool.cs      # Screen text finding tool
│   │       └── GetTextCoordinatesTool.cs    # Text coordinates tool
│   └── Windows.Agent.csproj   # Project file
└── Windows.Agent.Test/    # Test project
    ├── DesktopToolsExtendedTest.cs  # Desktop tools extended tests
    ├── FileSystemToolsExtendedTest.cs # File system tools extended tests
    ├── OCRToolsExtendedTest.cs      # OCR tools extended tests
    ├── ToolTest.cs                  # Basic tool tests
    ├── UIElementToolTest.cs         # UI element tool tests
    └── Windows.Agent.Test.csproj  # Test project file
```

## 🔧 Configuration

### Logging Configuration

CLI results go to stdout; logs/diagnostics go to stderr (so stdout JSON stays clean).

### Environment Variables

| Variable | Description | Default |
|----------|-------------|----------|
| `ASPNETCORE_ENVIRONMENT` | Runtime environment | `Production` |

## 📝 License

This project is open source under the MIT License. See the [LICENSE](LICENSE) file for details.

## 🔗 Related Links

- [.NET Documentation](https://docs.microsoft.com/dotnet/)
- [Windows API Documentation](https://docs.microsoft.com/windows/win32/)

## 🤝 Contributing Guide

We welcome community contributions! If you want to contribute to the project, please follow these steps:

### Development Environment Setup

1. **Clone Repository**
   ```bash
   git clone https://github.com/duanyunlun/Windows-Agent.NET.git
   cd Windows-Agent.NET
   ```

2. **Install Dependencies**
   ```bash
   dotnet restore
   ```

3. **Run Tests**
   ```bash
   dotnet test
   ```

4. **Build Project**
   ```bash
   dotnet build
   ```

### Contribution Process

1. Fork this repository
2. Create a feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to branch (`git push origin feature/AmazingFeature`)
5. Create a Pull Request

### Code Standards

- Follow C# coding conventions
- Add unit tests for new features
- Update relevant documentation
- Ensure all tests pass

### Issue Reporting

When reporting issues, please provide:
- Operating system version
- .NET version
- Detailed error information
- Steps to reproduce

## 📞 Support

If you encounter issues or have suggestions, please:

1. Check [Issues](https://github.com/duanyunlun/Windows-Agent.NET/issues)
2. Create a new Issue
3. Participate in discussions
4. Check [Wiki](https://github.com/duanyunlun/Windows-Agent.NET/wiki) for more help

---

**Note**: This tool requires appropriate Windows permissions to perform desktop automation operations. Please ensure use in a trusted environment.

**Disclaimer**: When using this tool for automation operations, please comply with relevant laws, regulations, and software usage agreements. Developers are not responsible for any consequences arising from misuse of the tool.
