# WinUI Desktop Application Project

Verdure.Assistant.WinUI is a modern Windows desktop application built with WinUI 3 framework, providing an intuitive graphical user interface for voice assistant interaction.

## Project Overview

### Design Goals

- **🖥️ Modern UI**: WinUI 3 design language for smooth user experience
- **🎤 Visual Voice**: Real-time voice waveform display and status feedback
- **⚙️ Easy Configuration**: Graphical configuration interface
- **📊 Status Monitoring**: Intuitive connection and service status display
- **🔄 Real-time Interaction**: Continuous dialogue and manual voice control support

## Quick Start

### Requirements

- **Windows 10 version 1809** (10.0; Build 17763) or higher
- **Windows 11** (recommended)
- **.NET 10.0 SDK**
- **Visual Studio 2026** 17.8 or higher
- **Windows App SDK** (automatically installed via NuGet)

### Running Locally

1. **Clone and navigate to project**

```bash
git clone https://github.com/maker-community/Verdure.Assistant.git
cd Verdure.Assistant/src/Verdure.Assistant.WinUI
```

2. **Open with Visual Studio**

```bash
# Open project file
start Verdure.Assistant.WinUI.csproj

# Or open entire solution
start ../../Verdure.Assistant.sln
```

3. **Set as startup project**

In Visual Studio:
- Right-click `Verdure.Assistant.WinUI` project
- Select "Set as Startup Project"
- Ensure target framework is `net10.0-windows10.0.19041.0`

4. **Run the project**

Press F5 in Visual Studio or run:

```bash
dotnet run
```

## User Interface Design

### Main Window Layout

```xml
<NavigationView x:Name="NavigationViewControl">
    <NavigationView.MenuItems>
        <NavigationViewItem Content="Home" Tag="HomePage">
            <NavigationViewItem.Icon>
                <FontIcon Glyph="&#xE80F;"/>
            </NavigationViewItem.Icon>
        </NavigationViewItem>
        
        <NavigationViewItem Content="Settings" Tag="SettingsPage">
            <NavigationViewItem.Icon>
                <FontIcon Glyph="&#xE713;"/>
            </NavigationViewItem.Icon>
        </NavigationViewItem>
    </NavigationView.MenuItems>
    
    <Frame x:Name="ContentFrame" Margin="24"/>
</NavigationView>
```

### Home Page Design

```xml
<ScrollViewer>
    <StackPanel Spacing="24" Padding="24">
        
        <!-- Connection Status Card -->
        <Border Background="{ThemeResource CardBackgroundFillColorDefaultBrush}">
            <Grid>
                <TextBlock Text="Connection Status"/>
                <Button Content="Connect" Command="{x:Bind ViewModel.ToggleConnectionCommand}"/>
            </Grid>
        </Border>
        
        <!-- Voice Control Area -->
        <Border Background="{ThemeResource CardBackgroundFillColorDefaultBrush}">
            <StackPanel Spacing="16">
                <TextBlock Text="Voice Interaction"/>
                
                <!-- Voice Waveform Display -->
                <controls:VoiceWaveform Height="80" 
                                      IsActive="{x:Bind ViewModel.IsVoiceActive, Mode=OneWay}"/>
                
                <!-- Voice Control Button -->
                <Button Width="60" Height="60"
                        Command="{x:Bind ViewModel.ToggleVoiceCommand}"/>
                
            </StackPanel>
        </Border>
        
    </StackPanel>
</ScrollViewer>
```

## Core Implementation

### HomePageViewModel

```csharp
public partial class HomePageViewModel : ViewModelBase
{
    private readonly IVoiceChatService _voiceChatService;
    
    [ObservableProperty]
    private ObservableCollection<ChatMessage> _chatMessages = new();
    
    [ObservableProperty]
    private ConnectionStatus _connectionStatus = ConnectionStatus.Disconnected;
    
    [ObservableProperty]
    private bool _isVoiceActive;
    
    public HomePageViewModel(IVoiceChatService voiceChatService)
    {
        _voiceChatService = voiceChatService;
        _voiceChatService.ConnectionStatusChanged += OnConnectionStatusChanged;
        _voiceChatService.MessageReceived += OnMessageReceived;
    }
    
    [RelayCommand]
    private async Task ToggleVoiceAsync()
    {
        if (IsVoiceActive)
        {
            await _voiceChatService.StopVoiceChatAsync();
        }
        else
        {
            await _voiceChatService.StartVoiceChatAsync();
        }
    }
    
    private void OnMessageReceived(object sender, ChatMessage message)
    {
        DispatcherQueue.GetForCurrentThread().TryEnqueue(() =>
        {
            ChatMessages.Add(message);
        });
    }
}
```

### Custom Controls

#### Voice Waveform Control

```csharp
public sealed partial class VoiceWaveform : UserControl
{
    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }
    
    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(VoiceWaveform));
    
    private void StartAnimation()
    {
        // Start waveform animation
        WaveAnimationStoryboard.Begin();
    }
    
    private void StopAnimation()
    {
        // Stop animation and reset to static state
        WaveAnimationStoryboard.Stop();
    }
}
```

## Themes and Styles

### Custom Styles

```xml
<Style x:Key="CircleButtonStyle" TargetType="Button">
    <Setter Property="Width" Value="60"/>
    <Setter Property="Height" Value="60"/>
    <Setter Property="CornerRadius" Value="30"/>
    <Setter Property="Background" Value="{ThemeResource AccentFillColorDefaultBrush}"/>
</Style>
```

### Theme Service

```csharp
public class ThemeService : IThemeService
{
    public async Task ApplyThemeAsync(AppTheme theme)
    {
        var rootFrame = Window.Current?.Content as FrameworkElement;
        if (rootFrame != null)
        {
            rootFrame.RequestedTheme = theme switch
            {
                AppTheme.Light => ElementTheme.Light,
                AppTheme.Dark => ElementTheme.Dark,
                AppTheme.System => ElementTheme.Default,
                _ => ElementTheme.Default
            };
        }
    }
}
```

## App Packaging and Distribution

### App Manifest Configuration

```xml
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
  <Identity Name="VerdureAssistant" 
            Publisher="CN=MakerCommunity" 
            Version="1.0.0.0"/>

  <Properties>
    <DisplayName>Verdure Assistant</DisplayName>
    <Description>Intelligent Voice Assistant based on .NET 10</Description>
  </Properties>

  <Applications>
    <Application Id="App" Executable="$targetnametoken$.exe">
      <uap:VisualElements DisplayName="Verdure Assistant"
                          Square150x150Logo="Assets\Square150x150Logo.png"/>
    </Application>
  </Applications>

  <Capabilities>
    <Capability Name="internetClient"/>
    <DeviceCapability Name="microphone"/>
  </Capabilities>
</Package>
```

### Build Script

```powershell
# Build and package the application
param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64"
)

dotnet build $ProjectPath -c $Configuration -p:Platform=$Platform
dotnet publish $ProjectPath -c $Configuration -p:Platform=$Platform -o $OutputPath\publish

# Create MSIX package
msbuild $ProjectPath /p:Configuration=$Configuration /p:GenerateAppxPackageOnBuild=true
```

## Advanced Features

### System Tray Integration

```csharp
public class SystemTrayService : ISystemTrayService
{
    private TaskbarIcon _taskbarIcon;
    
    private void InitializeTrayIcon()
    {
        _taskbarIcon = new TaskbarIcon
        {
            IconSource = new BitmapImage(new Uri("ms-appx:///Assets/tray-icon.ico")),
            ToolTipText = "Verdure Assistant"
        };
        
        _taskbarIcon.TrayLeftMouseDown += (s, e) => ShowMainWindow();
    }
    
    private void ShowMainWindow()
    {
        var mainWindow = Application.Current.MainWindow;
        mainWindow?.Show();
        mainWindow?.Activate();
    }
}
```

## Related Resources

- **[MAUI Project](/en/projects/maui)** - Cross-platform mobile app version
- **[Console Project](/en/projects/console)** - Command-line version
- **[API Project](/en/projects/api)** - Backend service
- **[Visual Studio Development](/en/development/visual-studio)** - VS development tips

By learning the WinUI project, you will master:
- Modern Windows app development
- WinUI 3 framework usage
- MVVM architecture pattern
- Data binding and commands
- Custom control development
- App packaging and distribution

These skills will help you develop professional-grade Windows desktop applications.