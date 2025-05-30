using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using XiaoZhi.Core.Interfaces;

namespace XiaoZhi.Core.Services;

/// <summary>
/// Global hotkey service for F3 key interrupt functionality
/// Based on the Python py-xiaozhi keyboard interrupt implementation
/// Uses Windows API directly without System.Windows.Forms dependency
/// </summary>
public class GlobalHotkeyService : IDisposable
{
    private readonly ILogger<GlobalHotkeyService>? _logger;
    private readonly IVoiceChatService _voiceChatService;
    
    // Windows API constants
    private const int WM_HOTKEY = 0x0312;
    private const int MOD_NONE = 0x0000;
    private const int VK_F3 = 0x72;
    
    // Hotkey ID
    private const int HOTKEY_ID = 9000;
    
    // Window for receiving hotkey messages
    private HotkeyWindow? _hotkeyWindow;
    private bool _isRegistered = false;

    public event EventHandler<bool>? HotkeyPressed;

    public GlobalHotkeyService(IVoiceChatService voiceChatService, ILogger<GlobalHotkeyService>? logger = null)
    {
        _voiceChatService = voiceChatService;
        _logger = logger;
    }

    public bool RegisterHotkey()
    {
        if (_isRegistered)
        {
            _logger?.LogWarning("F3 hotkey is already registered");
            return true;
        }

        try
        {
            _hotkeyWindow = new HotkeyWindow();
            _hotkeyWindow.HotkeyPressed += OnHotkeyPressed;
            
            _isRegistered = _hotkeyWindow.RegisterHotkey();
            
            if (_isRegistered)
            {
                _logger?.LogInformation("F3 global hotkey registered successfully");
            }
            else
            {
                _logger?.LogError("Failed to register F3 global hotkey");
            }
            
            return _isRegistered;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Exception while registering F3 hotkey");
            return false;
        }
    }

    public void UnregisterHotkey()
    {
        if (!_isRegistered || _hotkeyWindow == null)
            return;

        try
        {
            _hotkeyWindow.UnregisterHotkey();
            _hotkeyWindow.HotkeyPressed -= OnHotkeyPressed;
            _hotkeyWindow.Dispose();
            _hotkeyWindow = null;
            _isRegistered = false;
            
            _logger?.LogInformation("F3 global hotkey unregistered");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Exception while unregistering F3 hotkey");
        }
    }

    private void OnHotkeyPressed(object? sender, EventArgs e)
    {
        _logger?.LogInformation("F3 hotkey pressed - triggering voice chat interrupt");
        
        HotkeyPressed?.Invoke(this, true);
        
        // Automatically trigger voice chat stop
        _ = Task.Run(async () =>
        {
            try
            {
                if (_voiceChatService.IsVoiceChatActive)
                {
                    await _voiceChatService.StopVoiceChatAsync();
                    _logger?.LogInformation("Voice chat stopped due to F3 hotkey interrupt");
                }
                else
                {
                    _logger?.LogDebug("F3 pressed but voice chat is not active");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to stop voice chat after F3 hotkey");
            }
        });
    }

    public void Dispose()
    {
        UnregisterHotkey();
    }

    /// <summary>
    /// Message-only window class for receiving hotkey messages
    /// </summary>
    private class HotkeyWindow : IDisposable
    {
        private IntPtr _hWnd;
        private readonly WndProcDelegate _wndProcDelegate;
        
        public event EventHandler<EventArgs>? HotkeyPressed;

        public HotkeyWindow()
        {
            _wndProcDelegate = WndProc;
            CreateMessageOnlyWindow();
        }

        private void CreateMessageOnlyWindow()
        {
            // Create a message-only window
            _hWnd = CreateWindowEx(
                0, // dwExStyle
                "STATIC", // lpClassName
                "HotkeyWindow", // lpWindowName
                0, // dwStyle
                0, 0, 0, 0, // position and size
                HWND_MESSAGE, // hWndParent (message-only window)
                IntPtr.Zero, // hMenu
                IntPtr.Zero, // hInstance
                IntPtr.Zero // lpParam
            );

            if (_hWnd != IntPtr.Zero)
            {
                // Set our WndProc
                SetWindowLongPtr(_hWnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
            }
        }

        public bool RegisterHotkey()
        {
            if (_hWnd == IntPtr.Zero)
                return false;
                
            return RegisterHotKey(_hWnd, HOTKEY_ID, MOD_NONE, VK_F3);
        }

        public void UnregisterHotkey()
        {
            if (_hWnd != IntPtr.Zero)
            {
                UnregisterHotKey(_hWnd, HOTKEY_ID);
            }
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_HOTKEY)
            {
                var id = wParam.ToInt32();
                if (id == HOTKEY_ID)
                {
                    HotkeyPressed?.Invoke(this, EventArgs.Empty);
                }
            }

            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        public void Dispose()
        {
            UnregisterHotkey();
            
            if (_hWnd != IntPtr.Zero)
            {
                DestroyWindow(_hWnd);
                _hWnd = IntPtr.Zero;
            }
        }        // Windows API declarations
        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        
        private static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);
        private const int GWLP_WNDPROC = -4;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreateWindowEx(
            int dwExStyle, string lpClassName, string lpWindowName, int dwStyle,
            int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
            IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    }
}
