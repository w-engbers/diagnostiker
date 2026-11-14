using System.Text.Json;
using Diagnostiker.Kern;

namespace Diagnostiker.Tests;

public class WerkzeugeTests
{
    static string Wurzel => Path.GetFullPath(Path.Combine(ModulLader.ModulVerzeichnisFinden()!, ".."));

    [Fact]
    public void Sperrdatei_nennt_feste_Versionen_mit_Pruefsummen()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Wurzel, "werkzeuge", "werkzeuge.lock.json")));
        foreach (var w in doc.RootElement.GetProperty("werkzeuge").EnumerateArray())
        {
            Assert.Matches("^[0-9a-f]{64}$", w.GetProperty("installer").GetProperty("sha256").GetString()!);
            Assert.Matches("^[0-9a-f]{64}$", w.GetProperty("quelltext").GetProperty("sha256").GetString()!);
            Assert.Matches("^[0-9A-F]{40}$", w.GetProperty("signaturschluessel").GetString()!);
            Assert.False(string.IsNullOrWhiteSpace(w.GetProperty("lizenz").GetString()));
            Assert.Contains(w.GetProperty("dateien").EnumerateArray(), f => f.GetProperty("nach").GetString()!.EndsWith("smartctl.exe"));
        }
    }

    [Fact]
    public void SMART_aller_Datentraeger_gibt_es_auch_fuer_Windows_mit_dem_mitgelieferten_smartctl()
    {
        var (module, _) = ModulLader.Laden(ModulLader.ModulVerzeichnisFinden()!);
        var s = module.Single(m => m.Id == "platte.smart-alle").Schritte.Single(x => x.Os == "windows");
        Assert.Contains("{werkzeuge}", string.Join(' ', s.Argumente));
        Assert.True(s.Admin);
    }
}

public class WindowsKodierungTests
{
    /// <summary>Am 14.11.2026 zeigte ein Windows-Modul „Pr?fung“: Gibt ein PowerShell-Skript Umlaute aus, ohne die Ausgabe auf UTF-8 zu stellen,
    /// kommen sie in der Windows-Kodierung an und werden falsch gelesen.</summary>
    [Fact]
    public void PowerShell_Schritte_mit_Umlauten_stellen_die_Ausgabe_auf_UTF8()
    {
        var (module, _) = ModulLader.Laden(ModulLader.ModulVerzeichnisFinden()!);
        foreach (var m in module)
            foreach (var s in m.Schritte.Where(x => x.Os == "windows" && x.Befehl.StartsWith("powershell", StringComparison.OrdinalIgnoreCase)))
            {
                var skript = s.Argumente.Last();
                if (skript.Any(c => c > 127))
                    Assert.True(skript.Contains("OutputEncoding", StringComparison.Ordinal), $"{m.Id}: PowerShell-Skript mit Umlauten ohne OutputEncoding");
            }
    }
}
