# MAUI Cross-platform App Project

Verdure.Assistant.MAUI is a cross-platform mobile application built with .NET Multi-platform App UI (MAUI), supporting Android, iOS, Windows, and macOS platforms.

## Project Overview

### Design Goals

- **📱 Cross-platform Unity**: One codebase running on multiple platforms
- **🎨 Native Experience**: Platform-specific UI controls and interaction patterns
- **🔄 Offline Functionality**: Offline mode support and data synchronization
- **📊 Performance Optimized**: Memory and battery optimization for mobile devices
- **🔐 Secure & Reliable**: End-to-end encryption and data protection

### Supported Platforms

| Platform | Minimum Version | Recommended Version | Feature Support |
|----------|----------------|---------------------|-----------------|
| **Android** | API 21 (Android 5.0) | API 33+ (Android 13+) | Full features |
| **iOS** | iOS 11.0 | iOS 16.0+ | Full features |
| **Windows** | Windows 10 1809 | Windows 11 | Desktop experience |
| **macOS** | macOS 10.15 | macOS 13.0+ | Desktop experience |

## Quick Start

### Requirements

#### Universal Requirements
- **.NET 10.0 SDK**
- **Visual Studio 2026** or **Visual Studio Code**

#### Android Development
- **Android SDK** (API 21+)
- **Java Development Kit (JDK) 17**
- **Android Emulator** or physical device

#### iOS Development (macOS only)
- **Xcode 14+**
- **iOS SDK 11.0+**
- **Apple Developer Account** (for device deployment)

### Running the App

#### Android

```bash
# Build Android version
dotnet build -f net10.0-android

# Run in emulator
dotnet run -f net10.0-android

# Deploy to connected device
dotnet run -f net10.0-android --device "device-id"
```

#### iOS (macOS required)

```bash
# Build iOS version
dotnet build -f net10.0-ios

# Run in simulator
dotnet run -f net10.0-ios --simulator

# Deploy to device (requires developer certificate)
dotnet run -f net10.0-ios --device "device-udid"
```

## Project Configuration

### MauiProgram.cs

```csharp
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        // Register services
        builder.Services.AddSingleton<IVoiceChatService, VoiceChatService>();
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<MainPageViewModel>();
        
        return builder.Build();
    }
}
```

## Platform-Specific Implementation

### Android Implementation

#### Permissions (AndroidManifest.xml)

```xml
<uses-permission android:name="android.permission.RECORD_AUDIO" />
<uses-permission android:name="android.permission.INTERNET" />
<uses-permission android:name="android.permission.ACCESS_NETWORK_STATE" />
```

#### Android Audio Service

```csharp
#if ANDROID
public class AndroidAudioService : IPlatformAudioService
{
    public async Task<bool> RequestPermissionsAsync()
    {
        var status = await Permissions.RequestAsync<Permissions.Microphone>();
        return status == PermissionStatus.Granted;
    }
    
    public async Task StartRecordingAsync(int sampleRate = 16000, int channelCount = 1)
    {
        var hasPermission = await RequestPermissionsAsync();
        if (!hasPermission)
        {
            throw new UnauthorizedAccessException("Microphone permission not granted");
        }
        
        // Android-specific recording implementation
    }
}
#endif
```

### iOS Implementation

#### Info.plist Configuration

```xml
<key>NSMicrophoneUsageDescription</key>
<string>This app needs microphone access for voice interaction</string>
<key>NSSpeechRecognitionUsageDescription</key>
<string>This app needs speech recognition for understanding commands</string>
```

## User Interface Design

### Main Page Layout

```xml
<ContentPage x:Class="Verdure.Assistant.MAUI.Views.MainPage"
             Title="Verdure Assistant">
    
    <ScrollView>
        <VerticalStackLayout Spacing="20" Padding="20">
            
            <!-- Welcome Card -->
            <Frame BackgroundColor="{StaticResource Primary}" HasShadow="True">
                <Grid>
                    <Label Text="Welcome to Verdure Assistant" 
                           Style="{StaticResource HeaderLabelStyle}"
                           TextColor="White"/>
                    <Image Source="assistant_avatar.png" 
                           WidthRequest="80" HeightRequest="80"/>
                </Grid>
            </Frame>
            
            <!-- Quick Actions -->
            <Grid RowDefinitions="Auto,Auto" ColumnDefinitions="*,*">
                <Frame Grid.Row="0" Grid.Column="0">
                    <StackLayout>
                        <Image Source="mic_large.png"/>
                        <Label Text="Voice Chat"/>
                    </StackLayout>
                </Frame>
                
                <Frame Grid.Row="0" Grid.Column="1">
                    <StackLayout>
                        <Image Source="chat_large.png"/>
                        <Label Text="Text Chat"/>
                    </StackLayout>
                </Frame>
            </Grid>
            
        </VerticalStackLayout>
    </ScrollView>
    
</ContentPage>
```

## Custom Controls

### Chat Bubble Control

```xml
<ContentView x:Class="Verdure.Assistant.MAUI.Controls.ChatBubble">
    <Frame BackgroundColor="{Binding MessageBackgroundColor}"
           CornerRadius="15" Padding="12,8">
        <StackLayout>
            <Label Text="{Binding Content}" LineBreakMode="WordWrap"/>
            <Label Text="{Binding Timestamp, StringFormat='{0:HH:mm}'}"
                   FontSize="11" HorizontalOptions="End"/>
        </StackLayout>
    </Frame>
</ContentView>
```

## App Packaging and Distribution

### Android Packaging

#### Generate Signing Key

```bash
keytool -genkey -v -keystore verdure-assistant.keystore -alias verdure -keyalg RSA -keysize 2048 -validity 10000
```

#### Build APK

```bash
# Build Release APK
dotnet publish -f net10.0-android -c Release

# Build AAB (Android App Bundle)
dotnet publish -f net10.0-android -c Release -p:AndroidPackageFormat=aab
```

### iOS Packaging

#### Build IPA

```bash
# Build for App Store
dotnet publish -f net10.0-ios -c Release -p:RuntimeIdentifier=ios-arm64 -p:ArchiveOnBuild=true
```

## Testing and Debugging

### Unit Testing

```csharp
[TestClass]
public class VoicePageViewModelTests
{
    [TestMethod]
    public async Task ToggleRecording_WhenPermissionGranted_ShouldStartRecording()
    {
        // Arrange
        var mockAudioService = new Mock<IAudioService>();
        var viewModel = new VoicePageViewModel(mockAudioService.Object);
        
        // Act
        await viewModel.ToggleRecordingCommand.ExecuteAsync(null);
        
        // Assert
        Assert.IsTrue(viewModel.IsRecording);
    }
}
```

## Related Resources

- **[WinUI Project](/en/projects/winui)** - Windows desktop app version
- **[Console Project](/en/projects/console)** - Command-line version
- **[API Project](/en/projects/api)** - Backend service
- **[Visual Studio Development](/en/development/visual-studio)** - VS development environment setup

By learning the MAUI project, you will master:
- Cross-platform mobile app development
- .NET MAUI framework usage
- Platform-specific feature implementation
- Mobile app UI/UX design
- Permission management and security
- App packaging and distribution process