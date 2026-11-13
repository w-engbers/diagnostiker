using System.Text.Json;
using System.Text.Json.Serialization;

namespace Diagnostiker.Kern;

public static class ModulLader
{
    public static readonly JsonSerializerOptions JsonOptionen = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Sucht das Verzeichnis "module" ab dem Programmverzeichnis aufwärts (Stick-Wurzel oder Entwicklungsbaum).</summary>
    public static string? ModulVerzeichnisFinden(string? explizit = null)
    {
        if (explizit is not null) return Directory.Exists(explizit) ? explizit : null;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var kandidat = Path.Combine(dir.FullName, "module");
            if (Directory.Exists(kandidat)) return kandidat;
            dir = dir.Parent;
        }
        return null;
    }

    public static (List<ModulDefinition> Module, List<string> Fehler) Laden(string verzeichnis)
    {
        var module = new List<ModulDefinition>();
        var fehler = new List<string>();
        foreach (var datei in Directory.EnumerateFiles(verzeichnis, "*.json", SearchOption.AllDirectories).Order())
        {
            try
            {
                var m = JsonSerializer.Deserialize<ModulDefinition>(File.ReadAllText(datei), JsonOptionen);
                if (m is null || string.IsNullOrWhiteSpace(m.Id)) throw new InvalidDataException("Feld 'id' fehlt.");
                module.Add(m);
            }
            catch (Exception e)
            {
                fehler.Add($"{Path.GetFileName(datei)}: {e.Message}");
            }
        }
        return (module, fehler);
    }
}
