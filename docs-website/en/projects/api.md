# API Service Project (Raspberry Pi Robot)

Verdure.Assistant.Api is the server-side component designed for embedded devices like Raspberry Pi, providing RESTful API and WebSocket services.

## Project Overview

The API service is designed to be the core backend for intelligent voice assistant systems, particularly optimized for IoT devices and embedded environments.

### Core Features

- **🤖 Robot Backend**: Intelligent voice services for Raspberry Pi robots
- **📡 IoT Integration**: Support for IoT device connection and management
- **⚡ High Performance**: Optimized resource usage for embedded environments
- **🔄 Real-time Communication**: WebSocket support for bidirectional communication
- **🌐 Standard Interface**: RESTful API design for easy integration

## Quick Start

### Requirements

- **.NET 9.0 SDK**
- **Linux ARM64** (Raspberry Pi 4 recommended) or any .NET-supported system
- **At least 1GB RAM** (2GB+ recommended)
- **Network connection** (for voice service calls)

### Local Development

1. **Clone and navigate to project**

```bash
git clone https://github.com/maker-community/Verdure.Assistant.git
cd Verdure.Assistant/src/Verdure.Assistant.Api
```

2. **Configure appsettings.json**

```json
{
  "VoiceService": {
    "ServerUrl": "wss://api.tenclass.net/xiaozhi/v1/",
    "ApiKey": "your-api-key-here",
    "EnableVoice": true
  }
}
```

3. **Run the project**

```bash
dotnet run
```

## API Endpoints

### Voice Service Endpoints

#### Start Voice Session

```http
POST /api/voice/start
Content-Type: application/json

{
  "deviceId": "raspberry-pi-001",
  "userId": "user123",
  "sessionConfig": {
    "audioFormat": "opus",
    "sampleRate": 16000,
    "channels": 1
  }
}
```

#### Device Registration

```http
POST /api/device/register
Content-Type: application/json

{
  "deviceId": "raspberry-pi-001",
  "deviceName": "Living Room Assistant",
  "deviceType": "raspberry-pi",
  "capabilities": ["voice", "audio", "led"]
}
```

## Docker Deployment

### Dockerfile

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["src/Verdure.Assistant.Api/Verdure.Assistant.Api.csproj", "src/Verdure.Assistant.Api/"]
RUN dotnet restore "src/Verdure.Assistant.Api/Verdure.Assistant.Api.csproj"
COPY . .
WORKDIR "/src/src/Verdure.Assistant.Api"
RUN dotnet build "Verdure.Assistant.Api.csproj" -c Release -o /app/build
RUN dotnet publish "Verdure.Assistant.Api.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Verdure.Assistant.Api.dll"]
```

## Raspberry Pi Deployment

### System Setup

```bash
# Install .NET 9
wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
chmod +x ./dotnet-install.sh
./dotnet-install.sh --version latest --runtime aspnetcore

# Install audio support
sudo apt install -y alsa-utils portaudio19-dev
```

### Service Configuration

```bash
# Create systemd service
sudo tee /etc/systemd/system/verdure-api.service > /dev/null <<EOF
[Unit]
Description=Verdure Assistant API
After=network.target

[Service]
Type=notify
ExecStart=/opt/verdure-api/Verdure.Assistant.Api
Restart=always
User=pi
Environment=ASPNETCORE_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
EOF

sudo systemctl enable verdure-api
sudo systemctl start verdure-api
```

## Related Resources

- **[Console Project](/en/projects/console)** - Command-line client implementation
- **[WinUI Project](/en/projects/winui)** - Desktop client implementation
- **[Deployment Guide](/en/development/deployment)** - Detailed deployment instructions