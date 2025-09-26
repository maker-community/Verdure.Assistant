namespace Verdure.Assistant.Api.Services.WiFi;

/// <summary>
/// 本地化服务 - 支持多语言WiFi配置界面
/// </summary>
public class LocalizationService
{
    private string _currentLanguage = "zh";
    private readonly Dictionary<string, Dictionary<string, string>> _translations;

    public LocalizationService()
    {
        _translations = InitializeTranslations();
    }

    /// <summary>
    /// 设置语言
    /// </summary>
    public void SetLanguage(string languageCode)
    {
        if (_translations.ContainsKey(languageCode))
        {
            _currentLanguage = languageCode;
        }
    }

    /// <summary>
    /// 获取当前语言
    /// </summary>
    public string GetCurrentLanguage() => _currentLanguage;

    /// <summary>
    /// 获取字符串翻译
    /// </summary>
    public string GetString(string key)
    {
        if (_translations.ContainsKey(_currentLanguage) && _translations[_currentLanguage].ContainsKey(key))
        {
            return _translations[_currentLanguage][key];
        }

        // 回退到英文
        if (_translations.ContainsKey("en") && _translations["en"].ContainsKey(key))
        {
            return _translations["en"][key];
        }

        return key; // 如果找不到翻译，返回原始key
    }

    /// <summary>
    /// 获取所有可用语言
    /// </summary>
    public List<string> GetAvailableLanguages()
    {
        return _translations.Keys.ToList();
    }

    /// <summary>
    /// 获取语言显示名称
    /// </summary>
    public string GetLanguageDisplayName(string languageCode)
    {
        return languageCode switch
        {
            "zh" => "中文",
            "en" => "English",
            "de" => "Deutsch",
            "fr" => "Français",
            "ja" => "日本語",
            _ => languageCode
        };
    }

    /// <summary>
    /// 获取当前语言的所有字符串
    /// </summary>
    public Dictionary<string, string> GetAllStrings()
    {
        if (_translations.ContainsKey(_currentLanguage))
        {
            return _translations[_currentLanguage];
        }

        return _translations["en"]; // 回退到英文
    }

    /// <summary>
    /// 初始化翻译数据
    /// </summary>
    private Dictionary<string, Dictionary<string, string>> InitializeTranslations()
    {
        return new Dictionary<string, Dictionary<string, string>>
        {
            ["zh"] = new Dictionary<string, string>
            {
                ["Title"] = "绿荫助手 WiFi 配置",
                ["WelcomeMessage"] = "欢迎使用绿荫助手！请配置您的WiFi网络连接。",
                ["WifiName"] = "WiFi 网络名称",
                ["WifiNamePlaceholder"] = "请输入WiFi网络名称",
                ["WifiPassword"] = "WiFi 密码",
                ["WifiPasswordPlaceholder"] = "请输入WiFi密码",
                ["Connect"] = "连接",
                ["Language"] = "语言",
                ["WifiNameRequired"] = "WiFi网络名称不能为空",
                ["Error"] = "错误",
                ["BackLink"] = "返回",
                ["Success"] = "配置成功",
                ["SuccessMessage"] = "WiFi配置已保存，系统即将重启...",
                ["ConnectingTo"] = "正在连接到",
                ["RestartingMessage"] = "系统正在重启，请稍后...",
                ["ScanQrCode"] = "扫描二维码配置WiFi",
                ["OrVisit"] = "或访问",
                ["ConfigureWifi"] = "配置WiFi"
            },
            ["en"] = new Dictionary<string, string>
            {
                ["Title"] = "Verdure Assistant WiFi Setup",
                ["WelcomeMessage"] = "Welcome to Verdure Assistant! Please configure your WiFi network connection.",
                ["WifiName"] = "WiFi Network Name",
                ["WifiNamePlaceholder"] = "Enter WiFi network name",
                ["WifiPassword"] = "WiFi Password",
                ["WifiPasswordPlaceholder"] = "Enter WiFi password",
                ["Connect"] = "Connect",
                ["Language"] = "Language",
                ["WifiNameRequired"] = "WiFi network name cannot be empty",
                ["Error"] = "Error",
                ["BackLink"] = "Back",
                ["Success"] = "Configuration Successful",
                ["SuccessMessage"] = "WiFi configuration has been saved, system will restart soon...",
                ["ConnectingTo"] = "Connecting to",
                ["RestartingMessage"] = "System is restarting, please wait...",
                ["ScanQrCode"] = "Scan QR code to configure WiFi",
                ["OrVisit"] = "Or visit",
                ["ConfigureWifi"] = "Configure WiFi"
            },
            ["de"] = new Dictionary<string, string>
            {
                ["Title"] = "Verdure Assistant WiFi-Einrichtung",
                ["WelcomeMessage"] = "Willkommen bei Verdure Assistant! Bitte konfigurieren Sie Ihre WiFi-Netzwerkverbindung.",
                ["WifiName"] = "WiFi-Netzwerkname",
                ["WifiNamePlaceholder"] = "WiFi-Netzwerkname eingeben",
                ["WifiPassword"] = "WiFi-Passwort",
                ["WifiPasswordPlaceholder"] = "WiFi-Passwort eingeben",
                ["Connect"] = "Verbinden",
                ["Language"] = "Sprache",
                ["WifiNameRequired"] = "WiFi-Netzwerkname darf nicht leer sein",
                ["Error"] = "Fehler",
                ["BackLink"] = "Zurück",
                ["Success"] = "Konfiguration erfolgreich",
                ["SuccessMessage"] = "WiFi-Konfiguration wurde gespeichert, System wird bald neu gestartet...",
                ["ConnectingTo"] = "Verbindung zu",
                ["RestartingMessage"] = "System wird neu gestartet, bitte warten...",
                ["ScanQrCode"] = "QR-Code scannen, um WiFi zu konfigurieren",
                ["OrVisit"] = "Oder besuchen",
                ["ConfigureWifi"] = "WiFi konfigurieren"
            },
            ["fr"] = new Dictionary<string, string>
            {
                ["Title"] = "Configuration WiFi de l'Assistant Verdure",
                ["WelcomeMessage"] = "Bienvenue dans l'Assistant Verdure ! Veuillez configurer votre connexion réseau WiFi.",
                ["WifiName"] = "Nom du réseau WiFi",
                ["WifiNamePlaceholder"] = "Entrez le nom du réseau WiFi",
                ["WifiPassword"] = "Mot de passe WiFi",
                ["WifiPasswordPlaceholder"] = "Entrez le mot de passe WiFi",
                ["Connect"] = "Se connecter",
                ["Language"] = "Langue",
                ["WifiNameRequired"] = "Le nom du réseau WiFi ne peut pas être vide",
                ["Error"] = "Erreur",
                ["BackLink"] = "Retour",
                ["Success"] = "Configuration réussie",
                ["SuccessMessage"] = "La configuration WiFi a été sauvegardée, le système va bientôt redémarrer...",
                ["ConnectingTo"] = "Connexion à",
                ["RestartingMessage"] = "Le système redémarre, veuillez patienter...",
                ["ScanQrCode"] = "Scannez le code QR pour configurer le WiFi",
                ["OrVisit"] = "Ou visitez",
                ["ConfigureWifi"] = "Configurer le WiFi"
            },
            ["ja"] = new Dictionary<string, string>
            {
                ["Title"] = "Verdure アシスタント WiFi 設定",
                ["WelcomeMessage"] = "Verdure アシスタントへようこそ！WiFiネットワーク接続を設定してください。",
                ["WifiName"] = "WiFi ネットワーク名",
                ["WifiNamePlaceholder"] = "WiFiネットワーク名を入力",
                ["WifiPassword"] = "WiFi パスワード",
                ["WifiPasswordPlaceholder"] = "WiFiパスワードを入力",
                ["Connect"] = "接続",
                ["Language"] = "言語",
                ["WifiNameRequired"] = "WiFiネットワーク名は空にできません",
                ["Error"] = "エラー",
                ["BackLink"] = "戻る",
                ["Success"] = "設定完了",
                ["SuccessMessage"] = "WiFi設定が保存されました。システムは間もなく再起動します...",
                ["ConnectingTo"] = "接続中",
                ["RestartingMessage"] = "システムを再起動しています。お待ちください...",
                ["ScanQrCode"] = "QRコードをスキャンしてWiFiを設定",
                ["OrVisit"] = "または訪問",
                ["ConfigureWifi"] = "WiFi設定"
            }
        };
    }
}

/// <summary>
/// 语言项模型 - 用于模板渲染
/// </summary>
public class LanguageItem
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}