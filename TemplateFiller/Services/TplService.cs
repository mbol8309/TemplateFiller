// Services/TplService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;
using TemplateFiller.Models;

namespace TemplateFiller.Services;

public class TplService
{
    public static readonly string TemplatesFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "PDFTemplates");

    public TplService()
    {
        Directory.CreateDirectory(TemplatesFolder);
    }

    public List<TemplateInfo> GetAllTemplates()
    {
        var result = new List<TemplateInfo>();
        foreach (var file in Directory.GetFiles(TemplatesFolder, "*.tpl"))
        {
            try
            {
                var config = ReadConfig(file);
                result.Add(new TemplateInfo
                {
                    Name = config.Name,
                    FilePath = file,
                    FieldCount = config.Fields.Count,
                });
            }
            catch { /* skip corrupted */ }
        }
        return result;
    }

    public async Task CreateAsync(string templateName, string sourcePdfPath)
    {
        var tplPath = Path.Combine(TemplatesFolder, Slugify(templateName) + ".tpl");
        var config = new TemplateConfig { Name = templateName };

        using var zip = ZipFile.Open(tplPath, ZipArchiveMode.Create);

        // Añadir PDF
        zip.CreateEntryFromFile(sourcePdfPath, "template.pdf", CompressionLevel.Fastest);

        // Añadir config.json
        var configJson = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        var entry = zip.CreateEntry("config.json");
        using var writer = new StreamWriter(entry.Open());
        await writer.WriteAsync(configJson);
    }

    public TemplateConfig ReadConfig(string tplPath)
    {
        using var zip = ZipFile.OpenRead(tplPath);
        var entry = zip.GetEntry("config.json")
            ?? throw new InvalidOperationException("config.json no encontrado en el .tpl");
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return JsonSerializer.Deserialize<TemplateConfig>(json)
            ?? throw new InvalidOperationException("config.json inválido");
    }

    public async Task<string> ExtractPdfAsync(string tplPath)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TemplateFiller", Path.GetFileNameWithoutExtension(tplPath));
        Directory.CreateDirectory(tempDir);
        var pdfPath = Path.Combine(tempDir, "template.pdf");

        using var zip = ZipFile.OpenRead(tplPath);
        var entry = zip.GetEntry("template.pdf")
            ?? throw new InvalidOperationException("template.pdf no encontrado en el .tpl");

        using var src = entry.Open();
        using var dst = File.Create(pdfPath);
        await src.CopyToAsync(dst);

        return pdfPath;
    }

    public async Task SaveConfigAsync(string tplPath, TemplateConfig config)
    {
        // Leer todo el zip, reemplazar config.json, volver a escribir
        var tempPath = tplPath + ".tmp";
        using (var srcZip = ZipFile.OpenRead(tplPath))
        using (var dstZip = ZipFile.Open(tempPath, ZipArchiveMode.Create))
        {
            foreach (var entry in srcZip.Entries)
            {
                if (entry.FullName == "config.json") continue;
                var dstEntry = dstZip.CreateEntry(entry.FullName, CompressionLevel.Fastest);
                using var src = entry.Open();
                using var dst = dstEntry.Open();
                await src.CopyToAsync(dst);
            }

            var configJson = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            var configEntry = dstZip.CreateEntry("config.json");
            using var writer = new StreamWriter(configEntry.Open());
            await writer.WriteAsync(configJson);
        }

        File.Replace(tempPath, tplPath, null);
    }

    public void Delete(string tplPath) => File.Delete(tplPath);

    private static string Slugify(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name.ToLower().Trim(), @"[^a-z0-9]+", "-").Trim('-');
}
