using System;
using System.Threading.Tasks;

namespace Verdure.Assistant.Core.Interfaces
{
    /// <summary>
    /// 泛型设置服务接口
    /// 支持不同类型的设置对象的加载、保存、导入导出等操作
    /// </summary>
    /// <typeparam name="T">设置对象类型</typeparam>
    public interface ISettingsService<T> where T : class, new()
    {
        /// <summary>
        /// 加载设置
        /// </summary>
        /// <returns>设置对象</returns>
        Task<T> LoadSettingsAsync();

        /// <summary>
        /// 保存设置
        /// </summary>
        /// <param name="settings">要保存的设置对象</param>
        Task SaveSettingsAsync(T settings);

        /// <summary>
        /// 导出设置到文件
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <param name="settings">要导出的设置对象</param>
        /// <returns>是否导出成功</returns>
        Task<bool> ExportSettingsAsync(string filePath, T settings);

        /// <summary>
        /// 从文件导入设置
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>导入的设置对象，失败时返回null</returns>
        Task<T?> ImportSettingsAsync(string filePath);

        /// <summary>
        /// 重置为默认设置
        /// </summary>
        Task ResetToDefaultAsync();

        /// <summary>
        /// 获取当前设置（同步）
        /// </summary>
        T GetCurrentSettings();

        /// <summary>
        /// 设置变化事件
        /// </summary>
        event EventHandler<T>? SettingsChanged;
    }
}
