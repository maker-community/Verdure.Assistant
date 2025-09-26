# Debugging and Troubleshooting

Comprehensive debugging and troubleshooting guide for Verdure Assistant.

## Common Issues

### Connection Problems

**Issue**: Cannot connect to server

**Solutions**:
1. Check network connectivity
2. Verify server address configuration
3. Confirm firewall settings
4. Test with different endpoints

```bash
# Test connectivity
ping api.tenclass.net
telnet api.tenclass.net 443
```

### Audio Issues

**Issue**: Voice functionality not working

**Solutions**:
1. Check microphone permissions
2. Verify audio device configuration  
3. Test audio drivers
4. Ensure proper sample rate settings

```csharp
// Audio device verification
var audioDevices = await AudioService.GetAvailableDevicesAsync();
Console.WriteLine($"Available devices: {string.Join(", ", audioDevices.Select(d => d.Name))}");
```

### Build Issues

**Issue**: Compilation errors

**Solutions**:
1. Clean and rebuild: `dotnet clean && dotnet build`
2. Check .NET version: `dotnet --version`
3. Update Visual Studio to latest version
4. Verify project references

## Log Analysis

### Enable Verbose Logging

Configure detailed logging in `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Information"
    },
    "Console": {
      "IncludeScopes": true,
      "TimestampFormat": "yyyy-MM-dd HH:mm:ss.fff "
    }
  }
}
```

### Log File Locations

- **Windows**: `%LOCALAPPDATA%/Verdure.Assistant/logs/`
- **Linux**: `~/.local/share/Verdure.Assistant/logs/`
- **macOS**: `~/Library/Application Support/Verdure.Assistant/logs/`

### Structured Logging

Use structured logging for better analysis:

```csharp
_logger.LogInformation("Voice chat started for user {UserId} with device {DeviceId}", 
    userId, deviceId);
_logger.LogWarning("Connection timeout after {TimeoutMs}ms", timeoutMs);
_logger.LogError(ex, "Failed to process audio data for session {SessionId}", sessionId);
```

## Performance Analysis

### Memory Usage Monitoring

Monitor memory usage with built-in tools:

```csharp
public class MemoryMonitor
{
    public static void LogMemoryUsage(ILogger logger)
    {
        var gc = GC.GetTotalMemory(false);
        var workingSet = Environment.WorkingSet;
        
        logger.LogInformation("Memory usage - GC: {GCMemory:N0} bytes, WorkingSet: {WorkingSet:N0} bytes",
            gc, workingSet);
            
        if (workingSet > 500_000_000) // 500MB
        {
            logger.LogWarning("High memory usage detected: {WorkingSet:N0} bytes", workingSet);
            GC.Collect();
        }
    }
}
```

### Performance Profiling

Use .NET diagnostic tools:

```bash
# Collect performance trace
dotnet-trace collect --providers Microsoft-Extensions-Logging --process-id <PID>

# Monitor performance counters
dotnet-counters monitor --process-id <PID> --counters System.Runtime

# Create memory dump for analysis
dotnet-dump collect --process-id <PID>
```

### CPU Usage Analysis

```csharp
public class PerformanceProfiler
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    
    public void MeasureOperation(string operationName, Action operation)
    {
        var startTime = _stopwatch.ElapsedMilliseconds;
        
        try
        {
            operation();
        }
        finally
        {
            var elapsed = _stopwatch.ElapsedMilliseconds - startTime;
            if (elapsed > 1000) // Log operations taking longer than 1 second
            {
                _logger.LogWarning("Slow operation: {Operation} took {ElapsedMs}ms", 
                    operationName, elapsed);
            }
        }
    }
}
```

## Debugging Tools

### Visual Studio Debugger

#### Breakpoint Types

1. **Regular Breakpoints**: Standard pause points
2. **Conditional Breakpoints**: Break when condition is met
3. **Data Breakpoints**: Break when variable changes
4. **Tracepoints**: Log without stopping execution

#### Debug Windows

- **Locals**: View local variables
- **Watch**: Monitor specific expressions
- **Call Stack**: View method call hierarchy
- **Output**: See debug output and logs

### Command-Line Debugging Tools

#### dotnet-dump

Analyze memory dumps:

```bash
# Create dump
dotnet-dump collect -p <process-id>

# Analyze dump
dotnet-dump analyze <dump-file>
```

#### dotnet-trace

Performance tracing:

```bash
# Trace for 30 seconds
dotnet-trace collect -p <process-id> --duration 00:00:30

# View trace file
dotnet-trace convert <trace-file> --format speedscope
```

#### dotnet-counters

Real-time performance monitoring:

```bash
# Monitor standard counters
dotnet-counters monitor -p <process-id>

# Monitor custom counters
dotnet-counters monitor -p <process-id> --counters "Verdure.Assistant"
```

## Error Handling Best Practices

### Structured Exception Handling

```csharp
public async Task<Result<T>> ExecuteWithRetryAsync<T>(
    Func<Task<T>> operation, 
    int maxRetries = 3, 
    TimeSpan delay = default)
{
    for (int attempt = 1; attempt <= maxRetries; attempt++)
    {
        try
        {
            var result = await operation();
            return Result<T>.Success(result);
        }
        catch (Exception ex) when (IsTransientError(ex) && attempt < maxRetries)
        {
            _logger.LogWarning(ex, "Operation failed on attempt {Attempt}, retrying in {Delay}ms", 
                attempt, delay.TotalMilliseconds);
            
            await Task.Delay(delay);
            delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 1.5); // Exponential backoff
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Operation failed after {Attempts} attempts", attempt);
            return Result<T>.Failure(ex.Message);
        }
    }
    
    return Result<T>.Failure("Max retries exceeded");
}
```

### Global Exception Handler

```csharp
public class GlobalExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    
    public async Task HandleAsync(Exception exception, HttpContext context)
    {
        _logger.LogError(exception, "Unhandled exception occurred");
        
        var response = exception switch
        {
            ArgumentException => new { error = "Invalid arguments", details = exception.Message },
            UnauthorizedAccessException => new { error = "Unauthorized access", details = "Authentication required" },
            _ => new { error = "Internal server error", details = "An unexpected error occurred" }
        };
        
        context.Response.StatusCode = GetStatusCode(exception);
        await context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }
    
    private static int GetStatusCode(Exception exception) => exception switch
    {
        ArgumentException => 400,
        UnauthorizedAccessException => 401,
        FileNotFoundException => 404,
        _ => 500
    };
}
```

## Health Checks

### Application Health Monitoring

```csharp
builder.Services.AddHealthChecks()
    .AddCheck<VoiceServiceHealthCheck>("voice-service")
    .AddCheck<DatabaseHealthCheck>("database")
    .AddCheck<ExternalApiHealthCheck>("external-api");

// Health check implementation
public class VoiceServiceHealthCheck : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var isHealthy = await _voiceService.TestConnectionAsync();
            return isHealthy 
                ? HealthCheckResult.Healthy("Voice service is responding")
                : HealthCheckResult.Unhealthy("Voice service is not responding");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Voice service health check failed", ex);
        }
    }
}
```

## Troubleshooting Checklist

### Quick Diagnosis Steps

1. **Check Logs**: Review application logs for error messages
2. **Verify Configuration**: Ensure all settings are correct
3. **Test Connectivity**: Verify network and service connections
4. **Resource Usage**: Check CPU, memory, and disk usage
5. **Dependencies**: Verify all required services are running
6. **Permissions**: Ensure proper file and system permissions

### Common Fixes

```bash
# Clear temporary files
dotnet clean
rm -rf bin/ obj/

# Reset user settings
rm -rf ~/.local/share/Verdure.Assistant/
# Windows: rmdir /s "%LOCALAPPDATA%\Verdure.Assistant"

# Reinstall dependencies
dotnet restore --force

# Check for port conflicts
netstat -tulpn | grep :5000
# Windows: netstat -an | findstr :5000
```

## Related Resources

- **[Environment Setup](/en/guide/environment-setup)** - Initial setup requirements
- **[Performance Monitoring](/en/guide/tech-stack#performance-monitoring)** - Advanced monitoring techniques  
- **[Deployment Guide](/en/development/deployment)** - Production troubleshooting