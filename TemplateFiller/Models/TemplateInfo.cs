// Models/TemplateInfo.cs
namespace TemplateFiller.Models;

public class TemplateInfo
{
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string Description { get; set; } = "";
    public int FieldCount { get; set; }
}
