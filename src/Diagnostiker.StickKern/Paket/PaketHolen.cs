namespace Diagnostiker.StickKern.Paket;

/// <summary>Die zusätzlich vertrauten Schlüssel (aus einem Schlüsselwechsel), dauerhaft in einer Textdatei, ein Schlüssel je Zeile.</summary>
public sealed class VertrauensSpeicher(string datei)
{
    public IReadOnlyList<string> Lesen() =>
        File.Exists(datei) ? File.ReadAllLines(datei).Select(z => z.Trim()).Where(z => z.Length > 0 && !z.StartsWith('#')).ToList() : [];

    public void Hinzufuegen(string schluessel)
    {
        if (Lesen().Contains(schluessel.Trim())) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(datei))!);
        File.AppendAllText(datei, schluessel.Trim() + "\n");
    }

    /// <summary>Der eingebaute Schlüssel plus die gespeicherten.</summary>
    public Vertrauen Laden() => new(new[] { EingebauterSchluessel.Lesen() }.Concat(Lesen()));

    public static string Standard() =>
        Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x ? x : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
            "diagnostiker", "vertrauen.txt");
}

/// <summary>Ein vollständig heruntergeladenes und geprüftes Paket.</summary>
public sealed record GeholtesPaket(Manifest Manifest, string Ordner)
{
    public string BootDatei => Path.Combine(Ordner, Manifest.Boot.Name);
    public string DatenDatei => Path.Combine(Ordner, Manifest.Daten.Name);
}

/// <summary>
/// Holt ein Paket aus einer Quelle in den Zwischenspeicher und prüft es: erst die Signatur des Manifests, dann Größe und SHA-256 jeder Datei.
/// Bereits vorhandene, richtige Dateien werden nicht erneut geladen. Falsche Dateien werden gelöscht und nie weitergegeben.
/// </summary>
public sealed class PaketHolen(Vertrauen vertrauen, VertrauensSpeicher? speicher, string zwischenspeicher)
{
    public static string StandardZwischenspeicher() =>
        Path.Combine(Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { Length: > 0 } x ? x : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache"),
            "diagnostiker", "pakete");

    /// <summary>Nur Manifest und Signatur lesen und prüfen (für die Anzeige „neueste Version“), es wird nichts gespeichert.</summary>
    public async Task<(Manifest Manifest, IPaketQuelle Quelle)> ManifestPruefenAsync(IPaketQuelle quelle, CancellationToken ct)
    {
        var bytes = await quelle.LesenAsync(PaketBauer.ManifestName, ct);
        var sig = System.Text.Encoding.UTF8.GetString(await quelle.LesenAsync(PaketBauer.SignaturName, ct));
        var (m, neu) = vertrauen.Pruefen(bytes, sig);
        if (neu is not null) speicher?.Hinzufuegen(neu);
        return (m, quelle.FuerVersion(m.Version));
    }

    public async Task<GeholtesPaket> HolenAsync(IPaketQuelle quelle, Action<string> meldung, Action<string, long, long>? fortschritt, CancellationToken ct)
    {
        meldung($"Paketliste lesen ({quelle.Beschreibung}) …");
        var (m, fest) = await ManifestPruefenAsync(quelle, ct);
        meldung($"Version {m.Version} (erstellt {m.Erstellt:yyyy-MM-dd}): Die Signatur des Herausgebers ist in Ordnung.");

        var ordner = Path.Combine(zwischenspeicher, m.Version);
        Directory.CreateDirectory(ordner);

        // Platz im Zwischenspeicher (noch fehlende Dateien plus eine Reserve).
        var fehlend = m.Dateien.Where(d => !File.Exists(Path.Combine(ordner, d.Name)) || new FileInfo(Path.Combine(ordner, d.Name)).Length != d.Groesse).Sum(d => d.Groesse);
        var frei = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(ordner))!).AvailableFreeSpace;
        if (fehlend > 0 && frei < fehlend + (256L << 20))
            throw new PaketException($"Im Zwischenspeicher ({ordner}) ist zu wenig Platz: {Sicherheit.Groesse(fehlend)} nötig, {Sicherheit.Groesse(frei)} frei.");

        // Manifest und Signatur ablegen (zur Nachvollziehbarkeit).
        File.WriteAllBytes(Path.Combine(ordner, PaketBauer.ManifestName), await fest.LesenAsync(PaketBauer.ManifestName, ct));
        File.WriteAllBytes(Path.Combine(ordner, PaketBauer.SignaturName), await fest.LesenAsync(PaketBauer.SignaturName, ct));

        foreach (var d in m.Dateien)
        {
            var ziel = Path.Combine(ordner, d.Name);
            if (File.Exists(ziel) && new FileInfo(ziel).Length == d.Groesse && PruefOk(ziel, d))
            { meldung($"{d.Name}: bereits vorhanden und geprüft."); continue; }

            var teil = ziel + ".teil";
            meldung($"{d.Name} laden ({Sicherheit.Groesse(d.Groesse)}) …");
            await fest.HerunterladenAsync(d.Name, teil, d.Groesse, b => fortschritt?.Invoke(d.Name, b, d.Groesse), ct);
            meldung($"{d.Name} prüfen …");
            try { PaketPruefer.DateiPruefen(teil, d, b => fortschritt?.Invoke(d.Name + " prüfen", b, d.Groesse)); }
            catch (PaketException) { File.Delete(teil); throw; }
            File.Move(teil, ziel, overwrite: true);
        }

        // Ältere Versionen im Zwischenspeicher entfernen (je 1,2 GiB).
        foreach (var alt in Directory.GetDirectories(zwischenspeicher).Where(o => Path.GetFileName(o) != m.Version))
        { try { Directory.Delete(alt, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        return new GeholtesPaket(m, ordner);
    }

    static bool PruefOk(string datei, PaketDatei soll)
    {
        try { PaketPruefer.DateiPruefen(datei, soll); return true; }
        catch (PaketException) { File.Delete(datei); return false; }
    }
}
