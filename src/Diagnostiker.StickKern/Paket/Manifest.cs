using System.Text.Json;
using System.Text.Json.Serialization;

namespace Diagnostiker.StickKern.Paket;

/// <summary>Eine Datei eines Pakets. <c>Art</c>: „boot“ (gzip-Abbild bis zum Ende von DIAGBOOT) oder „daten“ (tar.gz mit dem Inhalt von DIAGDATEN).</summary>
public sealed record PaketDatei(string Name, string Art, long Groesse, string Sha256, long? EntpacktGroesse = null);

/// <summary>
/// Das Manifest eines Pakets (<c>manifest.json</c>). Es wird als Ganzes signiert (Datei <c>manifest.json.sig</c>); die Prüfsummen darin
/// sichern damit alle übrigen Dateien. <c>NaechsterSchluessel</c> erlaubt einen Schlüsselwechsel: Ein vertrauenswürdig signiertes Manifest
/// darf den Schlüssel nennen, dem künftig vertraut wird.
/// </summary>
public sealed record Manifest(
    int Format, string Version, DateTimeOffset Erstellt, int BootMib, IReadOnlyList<PaketDatei> Dateien, string? NaechsterSchluessel = null)
{
    public const int AktuellesFormat = 1;

    static readonly JsonSerializerOptions Optionen = new(JsonSerializerDefaults.Web) { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public byte[] ZuBytes() => JsonSerializer.SerializeToUtf8Bytes(this, Optionen);

    /// <summary>Liest ein Manifest und prüft die Form (Format, Dateinamen ohne Pfad, Prüfsummen, genau eine Datei je Art).</summary>
    public static Manifest Lesen(byte[] bytes)
    {
        Manifest? m;
        try { m = JsonSerializer.Deserialize<Manifest>(bytes, Optionen); }
        catch (JsonException e) { throw new PaketException("Das Manifest ist kein gültiges JSON: " + e.Message); }
        if (m is null) throw new PaketException("Das Manifest ist leer.");
        if (m.Format != AktuellesFormat) throw new PaketException($"Unbekanntes Paketformat {m.Format} (erwartet {AktuellesFormat}). Das Frontend ist zu alt oder zu neu für dieses Paket.");
        if (string.IsNullOrWhiteSpace(m.Version)) throw new PaketException("Das Manifest nennt keine Version.");
        if (m.BootMib is < 512 or > 16384) throw new PaketException("Ungültige Größe von DIAGBOOT im Manifest.");
        foreach (var d in m.Dateien)
        {
            if (d.Name.Length == 0 || d.Name.Contains('/') || d.Name.Contains('\\') || d.Name.StartsWith('.'))
                throw new PaketException($"Ungültiger Dateiname im Manifest: „{d.Name}“.");
            if (d.Sha256.Length != 64 || !d.Sha256.All(Uri.IsHexDigit)) throw new PaketException($"Ungültige Prüfsumme für {d.Name}.");
            if (d.Art is not ("boot" or "daten")) throw new PaketException($"Unbekannte Art „{d.Art}“ für {d.Name}.");
        }
        foreach (var art in new[] { "boot", "daten" })
            if (m.Dateien.Count(d => d.Art == art) != 1) throw new PaketException($"Das Paket muss genau eine Datei der Art „{art}“ enthalten.");
        return m;
    }

    [JsonIgnore] public PaketDatei Boot => Dateien.Single(d => d.Art == "boot");
    [JsonIgnore] public PaketDatei Daten => Dateien.Single(d => d.Art == "daten");
}

public sealed class PaketException(string meldung) : Exception(meldung);
