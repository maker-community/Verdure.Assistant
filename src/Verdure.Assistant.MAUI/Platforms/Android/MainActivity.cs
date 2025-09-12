using Android;
using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Microsoft.Extensions.Logging;

namespace Verdure.Assistant.MAUI.Platforms.Android;

[Activity(
    Theme = "@style/Maui.SplashTheme", 
    MainLauncher = true, 
    LaunchMode = LaunchMode.SingleTop, 
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private const int PERMISSIONS_REQUEST_CODE = 1001;
    
    // 简化权限配置，参考ForegroundService项目
    private readonly string[] _requiredPermissions = new[]
    {
        Manifest.Permission.RecordAudio,
        Manifest.Permission.ModifyAudioSettings,
        Manifest.Permission.ForegroundService,
        Manifest.Permission.Internet
    };

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        
        // 检查并请求基础权限（简化权限验证流程）
        CheckAndRequestBasicPermissions();
    }

    private void CheckAndRequestBasicPermissions()
    {
        var permissionsToRequest = new List<string>();

        // 检查基础权限
        foreach (var permission in _requiredPermissions)
        {
            if (ContextCompat.CheckSelfPermission(this, permission) != Permission.Granted)
            {
                permissionsToRequest.Add(permission);
            }
        }

        // Android 13+ 需要额外的通知权限（参考ForegroundService项目）
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            if (ContextCompat.CheckSelfPermission(this, Manifest.Permission.PostNotifications) != Permission.Granted)
            {
                permissionsToRequest.Add(Manifest.Permission.PostNotifications);
            }
        }

        if (permissionsToRequest.Count > 0)
        {
            ActivityCompat.RequestPermissions(this, permissionsToRequest.ToArray(), PERMISSIONS_REQUEST_CODE);
        }
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

        if (requestCode == PERMISSIONS_REQUEST_CODE)
        {
            var deniedPermissions = new List<string>();

            for (int i = 0; i < permissions.Length; i++)
            {
                if (grantResults[i] != Permission.Granted)
                {
                    deniedPermissions.Add(permissions[i]);
                }
            }

            if (deniedPermissions.Count > 0)
            {
                // 记录被拒绝的权限
                System.Diagnostics.Debug.WriteLine($"以下权限被拒绝: {string.Join(", ", deniedPermissions)}");

                // 显示权限说明对话框
                ShowPermissionRationale(deniedPermissions);
            }
            else
            {
                // 所有权限已授予
                System.Diagnostics.Debug.WriteLine("所有必要权限已授予");
            }
        }
    }

    private void ShowPermissionRationale(List<string> deniedPermissions)
    {
        var dialog = new AndroidX.AppCompat.App.AlertDialog.Builder(this)
            .SetTitle("权限说明")
            .SetMessage("绿荫助手需要以下基础权限：\n\n" +
                       "• 录音权限：语音输入功能\n" +
                       "• 前台服务权限：后台语音处理\n" +
                       "• 通知权限：服务状态提醒\n\n" +
                       "请授予这些权限以正常使用应用。")
            .SetPositiveButton("去设置", (sender, args) =>
            {
                // 打开应用设置页面
                var intent = new global::Android.Content.Intent(global::Android.Provider.Settings.ActionApplicationDetailsSettings);
                var uri = global::Android.Net.Uri.FromParts("package", PackageName, null);
                intent.SetData(uri);
                StartActivity(intent);
            })
            .SetNegativeButton("稍后", (sender, args) =>
            {
                // 用户选择稍后
                System.Diagnostics.Debug.WriteLine("用户选择稍后授予权限");
            })
            .SetCancelable(false);

        dialog.Show();
    }
}
