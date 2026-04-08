// Models/TemplateConfig.cs
using System.Collections.Generic;

namespace TemplateFiller.Models;

public class TemplateConfig
{
    public string Name { get; set; } = "";
    public int Version { get; set; } = 1;
    public List<FieldMapping> Fields { get; set; } = new();
    /// <summary>Cabeceras Excel guardadas para preview sin Excel cargado.</summary>
    public List<string> Columns { get; set; } = new();
    /// <summary>Primeras filas de ejemplo (máx 3) para preview en el editor.</summary>
    public List<List<string>> SampleRows { get; set; } = new();
}

public class FieldMapping
{
    public string Id { get; set; } = "";
    public int ColumnIndex { get; set; }
    public string ColumnName { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public int Page { get; set; }
    public float FontSize { get; set; } = 12f;
    public string FontColor { get; set; } = "#000000";
}
