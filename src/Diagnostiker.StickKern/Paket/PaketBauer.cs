using System.Formats.Tar;
using System.IO.Compression;

namespace Diagnostiker.StickKern.Paket;

/// <summary>
/// Herausgeberseite: macht aus einem rohen Boot-Abbild (<c>stick/boot-abbild-bauen.sh</c>) und dem Datenordner (<c>stick/staging.sh</c>)
/// ein Paket: <c>boot.img.gz</c>, <c>daten.tar.gz</c>, <c>manifest.json</c> und <c>manifest.json.sig</c>.
/// </summary>
public static class PaketBauer
{
    public const string BootName = "boot.img.gz";
    public const string DatenName = "daten.tar.gz";
    public const string ManifestName = "manifest.json";
    public const string SignaturName = "manifest.json.sig";

    public static Manifest Bauen(string bootAbbild, string datenOrdner, string ausgabe, string version, int bootMib,
        string privaterSchluessel, string? naechsterSchluessel = null, Action<string>? meldung = null, DateTimeOffset? zeit = null)
    {
        if (!File.Exists(bootAbbild)) throw new PaketException($"Boot-Abbild fehlt: {bootAbbild}");
        if (!Directory.Exists(datenOrdner)) throw new PaketException($"Datenordner fehlt: {datenOrdner}");
        if (string.IsNullOrWhiteSpace(version) || version.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_')))
            throw new PaketException("Die Version darf nur Buchstaben, Ziffern, Punkt, Bindestrich und Unterstrich enthalten.");
        Directory.CreateDirectory(ausgabe);
        var bootZiel = Path.Combine(ausgabe, BootName);
        var datenZiel = Path.Combine(ausgabe, DatenName);

        meldung?.Invoke("Boot-Abbild packen …");
        using (var ein = File.OpenRead(bootAbbild))
        using (var aus = File.Create(bootZiel))
        using (var gz = new GZipStream(aus, CompressionLevel.SmallestSize))
            ein.CopyTo(gz, 1 << 20);

        meldung?.Invoke("Datenpartition packen …");
        using (var aus = File.Create(datenZiel))
        using (var gz = new GZipStream(aus, CompressionLevel.SmallestSize))
            TarEinpacken(datenOrdner, gz);

        meldung?.Invoke("Prüfsummen und Signatur …");
        var m = new Manifest(Manifest.AktuellesFormat, version, zeit ?? DateTimeOffset.UtcNow, bootMib,
        [
            new(BootName, "boot", new FileInfo(bootZiel).Length, PaketPruefer.Sha256(bootZiel), new FileInfo(bootAbbild).Length),
            new(DatenName, "daten", new FileInfo(datenZiel).Length, PaketPruefer.Sha256(datenZiel)),
        ], naechsterSchluessel);
        var bytes = m.ZuBytes();
        File.WriteAllBytes(Path.Combine(ausgabe, ManifestName), bytes);
        File.WriteAllText(Path.Combine(ausgabe, SignaturName), Signatur.Signieren(bytes, privaterSchluessel) + "\n");
        return m;
    }

    /// <summary>Packt den Ordner in ein tar. Eigene Schleife statt <c>TarFile.CreateFromDirectory</c>: Das lässt versteckte Dateien
    /// (Punkt-Dateien) stillschweigend weg. Verweise sind nicht erlaubt (auf exFAT gibt es keine, und das Entpacken lehnt sie ab).</summary>
    static void TarEinpacken(string ordner, Stream ziel)
    {
        var wurzel = Path.GetFullPath(ordner);
        using var tw = new TarWriter(ziel, TarEntryFormat.Pax, leaveOpen: true);
        foreach (var pfad in Directory.EnumerateFileSystemEntries(wurzel, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var rel = Path.GetRelativePath(wurzel, pfad).Replace('\\', '/');
            var info = new FileInfo(pfad);
            if (info.LinkTarget is not null) throw new PaketException($"Im Datenordner ist ein Verweis nicht erlaubt: {rel}");
            if (Directory.Exists(pfad)) tw.WriteEntry(pfad, rel + "/");
            else tw.WriteEntry(pfad, rel);
        }
    }
}
