using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Models;

namespace Verdure.Assistant.Core.Services;

/// <summary>
/// 智能灯IoT设备 - 对应py-xiaozhi的Lamp
/// </summary>
public class LampIoTDevice : IoTDevice
{
    private readonly ILogger<LampIoTDevice> _deviceLogger;
    
    public LampIoTDevice(ILogger<LampIoTDevice> logger) : base(logger)
    {
        _deviceLogger = logger;
        Name = "Lamp";
        Description = "智能台灯";
        Type = "light";
    }
    
    protected override void RegisterProperties()
    {
        AddProperty("on", "灯的开关状态", IoTValueType.Boolean, true, true, false);
        AddProperty("brightness", "亮度(0-100)", IoTValueType.Number, true, true, 50);
        AddProperty("color", "颜色", IoTValueType.String, true, true, "white");
    }
    
    protected override void RegisterMethods()
    {
        AddMethod("TurnOn", "打开灯", 
            new List<IoTParameter>(),
            HandleTurnOn);
            
        AddMethod("TurnOff", "关闭灯", 
            new List<IoTParameter>(),
            HandleTurnOff);
            
        AddMethod("SetBrightness", "设置亮度", 
            new List<IoTParameter>
            {
                new("brightness", "亮度值(0-100)", IoTValueType.Number, true)
            },
            HandleSetBrightness);
            
        AddMethod("SetColor", "设置颜色", 
            new List<IoTParameter>
            {
                new("color", "颜色名称", IoTValueType.String, true)
            },
            HandleSetColor);
    }
    
    private async Task<object?> HandleTurnOn(Dictionary<string, IoTParameter> parameters)
    {
        SetPropertyValue("on", true);
        _deviceLogger.LogInformation("智能灯已打开");
        return new { status = "success", message = "灯已打开", on = true };
    }
    
    private async Task<object?> HandleTurnOff(Dictionary<string, IoTParameter> parameters)
    {
        SetPropertyValue("on", false);
        _deviceLogger.LogInformation("智能灯已关闭");
        return new { status = "success", message = "灯已关闭", on = false };
    }
    
    private async Task<object?> HandleSetBrightness(Dictionary<string, IoTParameter> parameters)
    {
        var brightness = parameters["brightness"].GetValue<double>();
        if (brightness < 0 || brightness > 100)
        {
            return new { status = "error", message = "亮度值必须在0-100之间" };
        }
        
        SetPropertyValue("brightness", brightness);
        _deviceLogger.LogInformation("智能灯亮度设置为: {Brightness}", brightness);
        return new { status = "success", message = $"亮度已设置为 {brightness}", brightness = brightness };
    }
    
    private async Task<object?> HandleSetColor(Dictionary<string, IoTParameter> parameters)
    {
        var color = parameters["color"].GetValue<string>();
        if (string.IsNullOrEmpty(color))
        {
            return new { status = "error", message = "颜色不能为空" };
        }
        
        SetPropertyValue("color", color);
        _deviceLogger.LogInformation("智能灯颜色设置为: {Color}", color);
        return new { status = "success", message = $"颜色已设置为 {color}", color = color };
    }
}

/// <summary>
/// 扬声器IoT设备 - 对应py-xiaozhi的Speaker
/// </summary>
public class SpeakerIoTDevice : IoTDevice
{
    private readonly ILogger<SpeakerIoTDevice> _deviceLogger;
    
    public SpeakerIoTDevice(ILogger<SpeakerIoTDevice> logger) : base(logger)
    {
        _deviceLogger = logger;
        Name = "Speaker";
        Description = "智能扬声器";
        Type = "speaker";
    }
    
    protected override void RegisterProperties()
    {
        AddProperty("volume", "音量(0-100)", IoTValueType.Number, true, true, 50);
        AddProperty("muted", "是否静音", IoTValueType.Boolean, true, true, false);
    }
    
    protected override void RegisterMethods()
    {
        AddMethod("SetVolume", "设置音量", 
            new List<IoTParameter>
            {
                new("volume", "音量值(0-100)", IoTValueType.Number, true)
            },
            HandleSetVolume);
            
        AddMethod("Mute", "静音", 
            new List<IoTParameter>(),
            HandleMute);
            
        AddMethod("Unmute", "取消静音", 
            new List<IoTParameter>(),
            HandleUnmute);
    }
    
    private async Task<object?> HandleSetVolume(Dictionary<string, IoTParameter> parameters)
    {
        var volume = parameters["volume"].GetValue<double>();
        if (volume < 0 || volume > 100)
        {
            return new { status = "error", message = "音量值必须在0-100之间" };
        }
        
        SetPropertyValue("volume", volume);
        SetPropertyValue("muted", false); // 设置音量时取消静音
        
        _deviceLogger.LogInformation("扬声器音量设置为: {Volume}", volume);
        
        // 这里可以调用系统音量控制API
        // 例如使用Windows API设置系统音量
        
        return new { status = "success", message = $"音量已设置为 {volume}", volume = volume };
    }
    
    private async Task<object?> HandleMute(Dictionary<string, IoTParameter> parameters)
    {
        SetPropertyValue("muted", true);
        _deviceLogger.LogInformation("扬声器已静音");
        return new { status = "success", message = "已静音", muted = true };
    }
    
    private async Task<object?> HandleUnmute(Dictionary<string, IoTParameter> parameters)
    {
        SetPropertyValue("muted", false);
        _deviceLogger.LogInformation("扬声器已取消静音");
        return new { status = "success", message = "已取消静音", muted = false };
    }
}
