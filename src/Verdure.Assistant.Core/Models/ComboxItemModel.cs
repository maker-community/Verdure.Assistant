namespace Verdure.Assistant.Core.Models;
public class ComboxItemModel
{
    public ComboxItemModel(string? dataKey, string? dataValue, object? tag = null)
    {
        DataKey = dataKey;
        DataValue = dataValue;
        Tag = tag;
    }
    public object? Tag
    {
        get; set;
    }
    public string? DataKey
    {
        get; set;
    }
    public string? DataValue
    {
        get; set;
    }
}
