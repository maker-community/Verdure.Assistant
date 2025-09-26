# Deployment Guide

Production deployment guide for Verdure Assistant projects.

## Deployment Methods

### Single-File Publishing

Create self-contained executables for different platforms:

```bash
# Windows deployment
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# Linux deployment  
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true

# macOS deployment
dotnet publish -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=true
```

### Framework-Dependent Deployment

Smaller deployment requiring .NET runtime on target system:

```bash
# Cross-platform framework-dependent deployment
dotnet publish -c Release --no-self-contained

# Platform-specific framework-dependent
dotnet publish -c Release -r linux-x64 --no-self-contained
```

## Docker Deployment

### Multi-stage Dockerfile

```dockerfile
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files
COPY ["src/Verdure.Assistant.Api/Verdure.Assistant.Api.csproj", "src/Verdure.Assistant.Api/"]
COPY ["src/Verdure.Assistant.Core/Verdure.Assistant.Core.csproj", "src/Verdure.Assistant.Core/"]

# Restore dependencies
RUN dotnet restore "src/Verdure.Assistant.Api/Verdure.Assistant.Api.csproj"

# Copy source code and build
COPY . .
WORKDIR "/src/src/Verdure.Assistant.Api"
RUN dotnet build "Verdure.Assistant.Api.csproj" -c Release -o /app/build
RUN dotnet publish "Verdure.Assistant.Api.csproj" -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Install audio dependencies for voice functionality
RUN apt-get update && apt-get install -y \
    alsa-utils \
    pulseaudio \
    && rm -rf /var/lib/apt/lists/*

# Copy published application
COPY --from=build /app/publish .

# Create non-root user for security
RUN useradd -m -s /bin/bash verdure && chown -R verdure:verdure /app
USER verdure

# Expose port
EXPOSE 80

# Set entry point
ENTRYPOINT ["dotnet", "Verdure.Assistant.Api.dll"]
```

### Docker Compose

```yaml
version: '3.8'

services:
  verdure-api:
    build:
      context: .
      dockerfile: src/Verdure.Assistant.Api/Dockerfile
    ports:
      - "5000:80"
      - "5001:443"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - VoiceService__ServerUrl=wss://api.tenclass.net/xiaozhi/v1/
      - ConnectionStrings__DefaultConnection=Server=db;Database=VerdureAssistant;
    volumes:
      - ./config:/app/config:ro
      - ./logs:/app/logs
    depends_on:
      - db
      - redis
    restart: unless-stopped

  db:
    image: postgres:15
    environment:
      - POSTGRES_DB=VerdureAssistant
      - POSTGRES_USER=verdure
      - POSTGRES_PASSWORD=secure_password
    volumes:
      - postgres_data:/var/lib/postgresql/data
    restart: unless-stopped

  redis:
    image: redis:alpine
    ports:
      - "6379:6379"
    volumes:
      - redis_data:/data
    restart: unless-stopped

volumes:
  postgres_data:
  redis_data:
```

## Service Deployment

### Windows Service

Install as Windows service:

```powershell
# Create Windows service
sc create VerdureAssistant binPath="C:\App\Verdure.Assistant.Console.exe" 
sc description VerdureAssistant "Verdure Assistant Voice Service"
sc start VerdureAssistant

# Configure service recovery
sc failure VerdureAssistant reset= 86400 actions= restart/5000/restart/5000/restart/5000
```

### Linux Service (systemd)

Create `/etc/systemd/system/verdure-assistant.service`:

```ini
[Unit]
Description=Verdure Assistant Voice Service
After=network.target
Wants=network-online.target

[Service]
Type=notify
ExecStart=/opt/verdure-assistant/Verdure.Assistant.Console
Restart=always
RestartSec=5
User=verdure
Group=verdure
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false
WorkingDirectory=/opt/verdure-assistant

# Security settings
NoNewPrivileges=yes
PrivateTmp=yes
ProtectSystem=strict
ProtectHome=yes
ReadWritePaths=/opt/verdure-assistant/logs

[Install]
WantedBy=multi-user.target
```

Enable and start the service:

```bash
sudo systemctl daemon-reload
sudo systemctl enable verdure-assistant
sudo systemctl start verdure-assistant
sudo systemctl status verdure-assistant
```

## Configuration Management

### Environment Variables

Set configuration via environment variables:

```bash
# Connection settings
export VoiceService__ServerUrl="wss://api.tenclass.net/xiaozhi/v1/"
export VoiceService__ApiKey="your-api-key-here"
export VoiceService__EnableVoice="true"

# Database connection
export ConnectionStrings__DefaultConnection="Server=localhost;Database=VerdureAssistant;User Id=verdure;Password=secure_password;"

# Logging
export Logging__LogLevel__Default="Information"
export Logging__LogLevel__Microsoft="Warning"
```

### Production Configuration

Create `appsettings.Production.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  "VoiceService": {
    "ServerUrl": "wss://api.tenclass.net/xiaozhi/v1/",
    "EnableVoice": true,
    "AudioSampleRate": 16000,
    "AudioChannels": 1,
    "ConnectionTimeout": 30
  },
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=VerdureAssistant;Integrated Security=true;"
  },
  "AllowedHosts": "*"
}
```

## Monitoring and Health Checks

### Application Health Checks

Configure health check endpoints:

```csharp
// Program.cs
builder.Services.AddHealthChecks()
    .AddCheck<VoiceServiceHealthCheck>("voice-service")
    .AddCheck<DatabaseHealthCheck>("database")
    .AddCheck("self", () => HealthCheckResult.Healthy("Application is running"));

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});
```

### Logging Configuration

Configure structured logging for production:

```csharp
builder.Services.AddSerilog((services, lc) => lc
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "VerdureAssistant")
    .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .WriteTo.File(
        "logs/app-.txt",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
);
```

### Monitoring Scripts

Create monitoring script for production:

```bash
#!/bin/bash
# monitor.sh - Production monitoring script

SERVICE_NAME="verdure-assistant"
LOG_FILE="/var/log/verdure-assistant-monitor.log"
HEALTH_URL="http://localhost:5000/health"

check_service() {
    if systemctl is-active --quiet $SERVICE_NAME; then
        echo "$(date): Service $SERVICE_NAME is running" >> $LOG_FILE
        return 0
    else
        echo "$(date): Service $SERVICE_NAME is not running" >> $LOG_FILE
        return 1
    fi
}

check_health() {
    if curl -f -s $HEALTH_URL > /dev/null; then
        echo "$(date): Health check passed" >> $LOG_FILE
        return 0
    else
        echo "$(date): Health check failed" >> $LOG_FILE
        return 1
    fi
}

restart_service() {
    echo "$(date): Restarting service $SERVICE_NAME" >> $LOG_FILE
    systemctl restart $SERVICE_NAME
    sleep 10
}

# Main monitoring loop
if ! check_service || ! check_health; then
    echo "$(date): Issues detected, restarting service" >> $LOG_FILE
    restart_service
fi

# Check disk space
DISK_USAGE=$(df / | tail -1 | awk '{print $5}' | sed 's/%//')
if [ $DISK_USAGE -gt 80 ]; then
    echo "$(date): Warning - Disk usage high: ${DISK_USAGE}%" >> $LOG_FILE
fi

# Check memory usage
MEMORY_USAGE=$(free | grep Mem | awk '{printf("%.2f", ($3/$2)*100)}')
echo "$(date): Memory usage: ${MEMORY_USAGE}%" >> $LOG_FILE
```

## Performance Optimization

### Runtime Configuration

Configure for production performance:

```json
{
  "runtimeOptions": {
    "configProperties": {
      "System.GC.Server": true,
      "System.GC.Concurrent": true,
      "System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization": false
    }
  }
}
```

### Publishing Optimizations

Use ReadyToRun compilation:

```xml
<PropertyGroup>
  <PublishReadyToRun>true</PublishReadyToRun>
  <PublishSingleFile>true</PublishSingleFile>
  <PublishTrimmed>true</PublishTrimmed>
  <TrimMode>link</TrimMode>
</PropertyGroup>
```

## Security Considerations

### HTTPS Configuration

Configure HTTPS for production:

```csharp
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
```

### Security Headers

Add security headers:

```csharp
app.Use(async (context, next) =>
{
    context.Response.Headers.Add("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Add("X-Frame-Options", "DENY");
    context.Response.Headers.Add("X-XSS-Protection", "1; mode=block");
    await next();
});
```

## Backup and Recovery

### Database Backups

Automated backup script:

```bash
#!/bin/bash
# backup.sh
BACKUP_DIR="/opt/backups/verdure-assistant"
DATE=$(date +%Y%m%d_%H%M%S)
DB_NAME="VerdureAssistant"

mkdir -p $BACKUP_DIR

# Database backup
pg_dump $DB_NAME | gzip > "$BACKUP_DIR/db_backup_$DATE.sql.gz"

# Configuration backup
tar -czf "$BACKUP_DIR/config_backup_$DATE.tar.gz" /opt/verdure-assistant/config/

# Clean old backups (keep last 7 days)
find $BACKUP_DIR -name "*.gz" -mtime +7 -delete
```

## Troubleshooting Production Issues

### Common Deployment Issues

1. **Port Binding Errors**
   ```bash
   # Check port usage
   netstat -tulpn | grep :5000
   lsof -i :5000
   ```

2. **Permission Issues**
   ```bash
   # Fix file permissions
   sudo chown -R verdure:verdure /opt/verdure-assistant
   sudo chmod +x /opt/verdure-assistant/Verdure.Assistant.Console
   ```

3. **Missing Dependencies**
   ```bash
   # Install .NET runtime
   sudo apt-get install -y dotnet-runtime-9.0
   
   # Install audio libraries
   sudo apt-get install -y alsa-utils pulseaudio
   ```

### Log Analysis

Monitor application logs:

```bash
# Follow application logs
sudo journalctl -u verdure-assistant -f

# Check recent errors
sudo journalctl -u verdure-assistant --since "1 hour ago" -p err

# View startup logs
sudo journalctl -u verdure-assistant --since today --until="5 minutes ago"
```

## Related Resources

- **[Environment Setup](/en/guide/environment-setup)** - Development environment requirements
- **[Debugging Guide](/en/development/debugging)** - Troubleshooting techniques
- **[API Documentation](/en/projects/api)** - API service specific deployment