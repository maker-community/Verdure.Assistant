# Console Application Project

Verdure.Assistant.Console is the command-line version of the project, providing the most direct and lightweight intelligent voice assistant experience.

## Project Overview

### Design Goals

- **🚀 Simple & Easy**: Minimal user interface focusing on core functionality
- **📝 Learning-Friendly**: Clear code structure for easy understanding
- **⚡ Efficient & Lightweight**: Minimal resource usage with fast startup
- **🔧 Script-Friendly**: Command-line arguments and batch operation support
- **🌐 Cross-platform**: Full compatibility on Windows, Linux, macOS

## Quick Start

### Requirements

- **.NET 9.0 SDK** or higher
- **Supported OS**: Windows 7+, Linux (most distributions), macOS 10.15+
- **Audio Support**: System needs microphone and speaker devices
- **Network Connection**: For voice service connectivity

### Running the Application

#### Direct Run

```bash
# Clone the project
git clone https://github.com/maker-community/Verdure.Assistant.git
cd Verdure.Assistant/src/Verdure.Assistant.Console

# Run
dotnet run
```

#### Run with Parameters

```bash
# Specify config file
dotnet run --config custom-config.json

# Enable verbose logging
dotnet run --verbose

# Auto-connect mode
dotnet run --auto-connect

# Batch mode (send single message and exit)
dotnet run --message "How's the weather today?" --exit
```

## User Interface

### Main Menu

Interactive menu displayed on startup:

```
===============================
    Verdure Assistant
    Version 1.0.0
===============================

Current Status: Disconnected
Server: wss://api.tenclass.net/xiaozhi/v1/

Please select an operation:
1. 🎤 Start voice chat
2. ⏹️  Stop voice chat
3. 🔄 Toggle chat state (auto mode)
4. ⚙️  Toggle auto dialogue mode
5. 📝 Send text message
6. 📊 View connection status
7. 🔧 Configuration settings
8. 📜 View chat history
9. 🔍 System information
0. ❌ Exit

Please enter option (0-9): _
```

### Voice Chat Interface

Interface after selecting voice chat:

```
🎤 Voice chat mode started

Current Status: Connecting...
Audio Device: Default Microphone -> Default Speaker
Sample Rate: 16000 Hz | Channels: Mono

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
💬 Chat History:

[10:30:15] 👤 User: Hello Xiaodian
[10:30:16] 🤖 Assistant: Hello! I'm Verdure Assistant, how can I help you?

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Status: 🎧 Listening... (say "Hello Xiaodian" to start conversation)

Press 'q' to exit voice mode | Press 's' to pause/resume | Press 'h' for help
```

## Core Implementation

### Program Entry Point

```csharp
class Program
{
    static async Task Main(string[] args)
    {
        // Parse command line arguments
        var options = CommandLineParser.Parse(args);
        
        // Build service container
        var host = CreateHostBuilder(args, options).Build();
        
        try
        {
            // Handle batch mode
            if (options.BatchMode)
            {
                await RunBatchModeAsync(options);
                return;
            }
            
            // Show welcome message
            ConsoleUI.ShowWelcomeMessage();
            
            // Run main loop
            await RunMainLoopAsync();
        }
        catch (Exception ex)
        {
            ConsoleUI.DisplayError($"Error: {ex.Message}");
        }
    }
}
```

### Console Service

```csharp
public class ConsoleService : IConsoleService
{
    public async Task<string> ShowMainMenuAsync()
    {
        Console.Clear();
        ConsoleUI.DisplayTitle("Verdure Assistant");
        
        // Display current status
        var status = _voiceChatService.IsConnected ? "Connected" : "Disconnected";
        ConsoleUI.DisplayStatus($"Current Status: {status}");
        
        // Display menu options
        var menuOptions = GetMainMenuOptions();
        _menuService.DisplayMenu(menuOptions);
        
        Console.Write("Please enter option (0-9): ");
        return Console.ReadLine()?.Trim() ?? "";
    }
}
```

### Voice Commands Handler

```csharp
public class VoiceCommands : ICommandHandler
{
    public async Task<CommandResult> HandleStartVoiceAsync()
    {
        try
        {
            ConsoleUI.DisplayInfo("Starting voice chat...");
            
            if (!_voiceChatService.IsConnected)
            {
                await _voiceChatService.ConnectAsync();
            }
            
            await _voiceChatService.StartVoiceChatAsync();
            ConsoleUI.DisplaySuccess("Voice chat started");
            
            // Enter voice chat loop
            await EnterVoiceChatLoopAsync();
            
            return CommandResult.Success("Voice chat ended");
        }
        catch (Exception ex)
        {
            return CommandResult.Error($"Failed to start voice chat: {ex.Message}");
        }
    }
}
```

## Command Line Arguments

### CommandLineParser

```csharp
public class CommandLineParser
{
    public static CommandLineOptions Parse(string[] args)
    {
        var options = new CommandLineOptions();
        
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLower())
            {
                case "--config":
                case "-c":
                    if (i + 1 < args.Length)
                    {
                        options.ConfigFile = args[++i];
                    }
                    break;
                    
                case "--verbose":
                case "-v":
                    options.Verbose = true;
                    break;
                    
                case "--auto-connect":
                case "-a":
                    options.AutoConnect = true;
                    break;
                    
                case "--message":
                case "-m":
                    if (i + 1 < args.Length)
                    {
                        options.Message = args[++i];
                        options.BatchMode = true;
                    }
                    break;
            }
        }
        
        return options;
    }
}
```

## Docker Deployment

### Dockerfile

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
COPY ["src/Verdure.Assistant.Console/Verdure.Assistant.Console.csproj", "src/Verdure.Assistant.Console/"]
RUN dotnet restore "src/Verdure.Assistant.Console/Verdure.Assistant.Console.csproj"
COPY . .
WORKDIR "/src/src/Verdure.Assistant.Console"
RUN dotnet build "Verdure.Assistant.Console.csproj" -c Release -o /app/build
RUN dotnet publish "Verdure.Assistant.Console.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Verdure.Assistant.Console.dll"]
```

## Unit Testing

```csharp
[TestClass]
public class ConsoleServiceTests
{
    [TestMethod]
    public async Task ConfirmActionAsync_WithYesInput_ReturnsTrue()
    {
        // Arrange
        using var sr = new StringReader("y\n");
        Console.SetIn(sr);
        
        // Act
        var result = await _consoleService.ConfirmActionAsync("Confirm action?");
        
        // Assert
        Assert.IsTrue(result);
    }
}
```

## Related Resources

- **[API Project](/en/projects/api)** - Server-side implementation
- **[WinUI Project](/en/projects/winui)** - Graphical interface version
- **[MAUI Project](/en/projects/maui)** - Mobile implementation
- **[Visual Studio Development](/en/development/visual-studio)** - Development environment setup

By learning the Console project, you will master:
- .NET console application development
- Command-line program design patterns
- User interaction and menu systems
- Cross-platform compatibility handling
- Docker containerization deployment
- Performance monitoring and optimization
- Unit testing practices

These skills are valuable for developing command-line tools, automation scripts, and server applications.