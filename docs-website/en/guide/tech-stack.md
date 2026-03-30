# Technology Stack

Overview of the technologies and frameworks used in Verdure Assistant.

## Core Technologies

### .NET 10 Platform

**.NET 10** is Microsoft's latest cross-platform development framework, providing:

- **Cross-platform compatibility** - Run on Windows, Linux, macOS
- **Performance optimizations** - JIT compiler improvements, faster startup
- **Memory management** - Improved garbage collector, reduced memory usage
- **Asynchronous programming** - Native support for async/await patterns

### C# 14 Language Features

Modern C# features used throughout the project:

#### Primary Constructors
```csharp
public class AudioService(ILogger<AudioService> logger)
{
    public void PlaySound() => logger.LogInformation("Playing sound...");
}
```

#### Collection Expressions
```csharp
var supportedFormats = ["wav", "mp3", "opus"];
```

## Audio Processing

### Opus Codec

**Opus** is the core audio codec technology:

- **High compression** - Reduces file size while maintaining quality
- **Low latency** - Ideal for real-time voice processing
- **Adaptive** - Dynamically adjusts encoding parameters
- **Cross-platform** - Consistent performance across platforms

## Communication Protocols

### WebSocket Real-time Communication

```csharp
public async Task SendAudioAsync(byte[] audioData)
{
    var message = CreateAudioMessage(audioData);
    await _webSocket.SendAsync(message, WebSocketMessageType.Binary);
}
```

### MQTT IoT Integration

```csharp
public async Task PublishVoiceCommandAsync(string command)
{
    var message = new MqttApplicationMessageBuilder()
        .WithTopic("verdure/voice/command")
        .WithPayload(command)
        .Build();
        
    await _mqttClient.PublishAsync(message);
}
```

## UI Frameworks

### WinUI 3 Desktop Apps

Modern Windows UI framework for desktop applications.

### .NET MAUI Cross-platform Apps

Single codebase for iOS, Android, Windows, and macOS.

## Architecture Patterns

### Dependency Injection

```csharp
builder.Services.AddSingleton<IAudioCodec, OpusCodec>();
builder.Services.AddScoped<IVoiceChatService, VoiceChatService>();
```

### MVVM Pattern

```csharp
[ObservableObject]
public partial class HomePageViewModel
{
    [ObservableProperty]
    private string _connectionStatus = "Disconnected";
    
    [RelayCommand]
    private async Task ToggleConnectionAsync()
    {
        // Command implementation
    }
}
```

## Testing Framework

### Unit Testing with xUnit

```csharp
[Fact]
public async Task StartVoiceChatAsync_ShouldConnectWebSocket()
{
    // Arrange
    _mockWebSocketClient.Setup(x => x.ConnectAsync(It.IsAny<Uri>()))
        .Returns(Task.CompletedTask);
    
    // Act
    await _service.StartVoiceChatAsync();
    
    // Assert
    _mockWebSocketClient.Verify(x => x.ConnectAsync(It.IsAny<Uri>()), Times.Once);
}
```

## Security Features

### Data Encryption

```csharp
public class EncryptionService : IEncryptionService
{
    public string Encrypt(string plainText)
    {
        using var aes = Aes.Create();
        // Encryption implementation
    }
}
```

## Learning Resources

### Official Documentation
- [.NET 10 Documentation](https://docs.microsoft.com/dotnet/core/whats-new/dotnet-10)
- [C# 14 Features](https://docs.microsoft.com/dotnet/csharp/whats-new/csharp-14)
- [WinUI 3 Documentation](https://docs.microsoft.com/windows/apps/winui/)

## Next Steps

- [Project Architecture](/en/guide/architecture) - Understand the system design
- [API Project](/en/projects/api) - Learn backend service development
- [WinUI Project](/en/projects/winui) - Master desktop app development