using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Verdure.Assistant.Core.Interfaces;

public interface IEmotionManager
{
    Task InitializeAsync();
    Task<string?> GetEmotionImageAsync(string emotion);
    string GetEmotionEmoji(string emotion);
    bool HasEmotionAsset(string emotion);
    IEnumerable<string> GetAvailableEmotions();
    void ClearCache();
}
