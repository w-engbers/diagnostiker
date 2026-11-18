using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Text.RegularExpressions;
using Diagnostiker.StickKern.Paket;

namespace Diagnostiker.StickKern;

/// <summary>
/// Schreibt einen Stick aus einem geprüften Paket, ohne Docker: ein privilegierter Helfer (eingebettetes Skript, über pkexec) schreibt das
/// Boot-Abbild und legt DIAGDATEN an; danach wird DIAGDATEN ohne Root eingehängt und mit dem Inhalt von <c>daten.tar.gz</c> gefüllt.
/// Vor dem Schreiben gelten dieselben Sicherungen wie beim Weg über das Docker-Skript (<see cref="ZielVorbereitung"/>), der Helfer prüft das Ziel
/// zusätzlich selbst.
/// </summary>
public sealed partial class PaketStickErsteller(ILaufwerkQuelle quelle, IBefehlsausfuehrer befehle)
{
    /// <summary>Programm, über das der Helfer mit Administratorrechten läuft (Standard pkexec; im Test eine Attrappe).</summary>
    public string Rechteerhoehung { get; init; } = "pkexec";
    /// <summary>Läuft das Programm schon als root, wird das Skript direkt gestartet.</summary>
    /// <summary>Sprachunabhängige Schrittmarken für die Fortschrittsanzeige („Datenpartition einhängen“, „Programme und Module“, „Daten sichern“).</summary>
    public Action<string>? SchrittMeldung { get; set; }
    public bool BinRoot { get; init; } = Environment.UserName == "root";
    /// <summary>Nur für Tests: Loop-Geräte zulassen.</summary>
    public bool Testlaufwerk { get; init; }
    public TimeSpan Pause { get; init; } = TimeSpan.FromSeconds(1);
    public int Nachpruefungen { get; init; } = 20;

    [GeneratedRegex(@"^SCHREIBEN_FERTIG (/dev/[A-Za-z0-9]+)$")] private static partial Regex Fertig();
    [GeneratedRegex(@"[Mm]ounted .+? at (?<ort>.+?)\.?$")] private static partial Regex Eingehaengt();
    [GeneratedRegex(@"already mounted at [`'‘""]?(?<ort>/[^'`’""]+)")] private static partial Regex SchonEingehaengt();

    public static string HelferSkript()
    {
        using var s = typeof(PaketStickErsteller).Assembly.GetManifestResourceStream("paket-schreiben.sh")
            ?? throw new PaketException(Sprache.T("Im Programm ist das Schreibskript nicht eingebaut."));
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    public async Task<ErstellErgebnis> ErstellenAsync(Laufwerk gewaehlt, string? eingabe, GeholtesPaket paket,
        Action<string> ausgabe, CancellationToken ct = default)
    {
        var (laufwerk, fehler) = await ZielVorbereitung.PruefenUndAushaengenAsync(quelle, befehle, gewaehlt, eingabe, ausgabe, ct);
        if (fehler is not null || laufwerk is null) return new ErstellErgebnis(false, fehler ?? Sprache.T("Unbekannter Fehler."));

        // Die Dateien des Pakets müssen unverändert sein (der Helfer prüft die Prüfsumme des Abbilds noch einmal als root).
        try
        {
            PaketPruefer.DateiPruefen(paket.BootDatei, paket.Manifest.Boot);
            PaketPruefer.DateiPruefen(paket.DatenDatei, paket.Manifest.Daten);
        }
        catch (PaketException e) { return new ErstellErgebnis(false, e.Message + " " + Sprache.T("Es wurde nichts verändert.")); }

        // --- 1. Schreiben mit Administratorrechten ---
        ausgabe(Sprache.T("Schreiben auf {0} (dafür wird einmal nach dem Passwort gefragt) …", laufwerk.Pfad));
        var skript = HelferSkript();
        string[] helferArgs = [laufwerk.Pfad, paket.BootDatei, paket.Manifest.Boot.Sha256, (paket.Manifest.Boot.EntpacktGroesse ?? 0).ToString(),
            .. Testlaufwerk ? new[] { "test" } : []];
        string[] gesamt = ["-c", skript, "bash", .. helferArgs];
        string? p3 = null;
        void Zeile(string z) { ausgabe(z); var t = Fertig().Match(z.Trim()); if (t.Success) p3 = t.Groups[1].Value; }

        int code;
        if (BinRoot) code = await befehle.AusfuehrenAsync("/bin/bash", gesamt, null, Zeile, ct);
        else code = await befehle.AusfuehrenAsync(Rechteerhoehung, ["/bin/bash", .. gesamt], null, Zeile, ct);

        if (code == 126) return new ErstellErgebnis(false, Sprache.T("Die Anmeldung wurde abgebrochen oder das Passwort war falsch. Es wurde nichts verändert."));
        if (code == 127 && p3 is null) return new ErstellErgebnis(false, Sprache.T("Die Administratorrechte konnten nicht angefordert werden (pkexec fehlt oder ist nicht eingerichtet). Es wurde nichts verändert."));
        if (code == 3) return new ErstellErgebnis(false, Sprache.T("Der Helfer hat das Laufwerk abgelehnt (siehe Meldung oben). Es wurde nichts verändert."));
        if (code == 4) return new ErstellErgebnis(false, Sprache.T("Das Schreiben ist fehlgeschlagen: Beim Zurücklesen stimmte der Inhalt des Sticks nicht mit dem Abbild überein. Der Stick ist vermutlich defekt. Bitte einen anderen Stick nehmen."));
        if (code == 5) return new ErstellErgebnis(false, Sprache.T("Das Schreiben ist fehlgeschlagen: Die Datenpartition erschien nicht. Stick abziehen, neu einstecken und es erneut versuchen."));
        if (code != 0 || p3 is null)
            return new ErstellErgebnis(false, Sprache.T("Das Schreiben ist fehlgeschlagen (Exitcode {0}). Der Stick ist nicht brauchbar, bitte erneut versuchen oder einen anderen Stick nehmen.", code));

        // --- 2. DIAGDATEN einhängen und füllen (ohne Root) ---
        SchrittMeldung?.Invoke("Datenpartition einhängen");
        ausgabe(Sprache.T("Datenpartition einhängen …"));
        var ort = await EinhaengenAsync(p3, laufwerk.Pfad, ausgabe, ct);
        if (ort is null) return new ErstellErgebnis(false, Sprache.T("DIAGDATEN ({0}) lässt sich nicht einhängen. Stick abziehen, neu einstecken und es erneut versuchen.", p3));

        try
        {
            SchrittMeldung?.Invoke("Programme und Module");
            ausgabe(Sprache.T("Programme und Module auf den Stick kopieren ({0}) …", ort));
            var n = Entpacken(paket.DatenDatei, ort, ausgabe);
            ausgabe(Sprache.T("{0} Dateien kopiert.", n));
            if (!File.Exists(Path.Combine(ort, "stick.json")))
                return new ErstellErgebnis(false, Sprache.T("Nach dem Kopieren fehlt stick.json auf dem Stick. Der Stick ist nicht brauchbar."));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or FormatException or PaketException)
        { return new ErstellErgebnis(false, Sprache.T("Das Kopieren auf den Stick ist fehlgeschlagen: {0}", e.Message)); }

        // --- 3. Aushängen, damit der Stick sicher abgezogen werden kann ---
        SchrittMeldung?.Invoke("Daten sichern");
        ausgabe(Sprache.T("Daten sichern und Stick aushängen …"));
        await befehle.AusfuehrenAsync("sync", [], null, _ => { }, ct);
        var u = await befehle.AusfuehrenAsync("udisksctl", ["unmount", "-b", p3], null, ausgabe, ct);

        // --- 4. Nachprüfung ---
        for (var i = 0; i < Nachpruefungen; i++)
        {
            var neu = await ZielVorbereitung.Suchen(quelle, laufwerk.Pfad, ct);
            var namen = neu?.Partitionen.Select(p => p.Bezeichnung).ToHashSet() ?? [];
            if (namen.Contains("DIAGBOOT") && namen.Contains("DIAGDATEN"))
                return new ErstellErgebnis(true, Sprache.T("Der Stick ist fertig (Version {0}).", paket.Manifest.Version) + " " +
                    (u == 0 ? Sprache.T("Er kann jetzt abgezogen werden.") : Sprache.T("Bitte vor dem Abziehen über das Dateiprogramm auswerfen.")));
            if (i < Nachpruefungen - 1 && Pause > TimeSpan.Zero) await Task.Delay(Pause, ct);
        }
        return new ErstellErgebnis(false, Sprache.T("Die Partitionen DIAGBOOT und DIAGDATEN sind nicht zu sehen. Stick abziehen und neu einstecken, dann erneut ansehen."));
    }

    /// <summary>Hängt die Partition ein (oder findet den Ort, wenn der Desktop sie schon eingehängt hat).</summary>
    async Task<string?> EinhaengenAsync(string partition, string laufwerkPfad, Action<string> ausgabe, CancellationToken ct)
    {
        for (var versuch = 0; versuch < 10; versuch++)
        {
            // Hat der Desktop sie schon selbst eingehängt?
            var l = await ZielVorbereitung.Suchen(quelle, laufwerkPfad, ct);
            var vorhanden = l?.Partitionen.FirstOrDefault(p => p.Pfad == partition)?.Einhaengepunkte.FirstOrDefault();
            if (vorhanden is not null) return vorhanden;

            string? ort = null;
            var code = await befehle.AusfuehrenAsync("udisksctl", ["mount", "-b", partition], null, z =>
            {
                ausgabe(z);
                var m = Eingehaengt().Match(z.Trim()); if (m.Success && ort is null) ort = m.Groups["ort"].Value;
                var s = SchonEingehaengt().Match(z); if (s.Success) ort ??= s.Groups["ort"].Value;
            }, ct);
            if (ort is not null) return ort;
            if (Pause > TimeSpan.Zero) await Task.Delay(Pause, ct);
        }
        return null;
    }

    /// <summary>Entpackt <c>daten.tar.gz</c> auf den Stick. Eigene Schleife statt <c>ExtractToDirectory</c>: Auf exFAT gibt es keine
    /// Unix-Rechte, das Setzen würde fehlschlagen. Nur Verzeichnisse und normale Dateien; Pfade außerhalb des Ziels werden abgelehnt.</summary>
    public static int Entpacken(string tarGz, string ziel, Action<string>? ausgabe = null)
    {
        var wurzel = Path.GetFullPath(ziel).TrimEnd('/') + "/";
        var anzahl = 0;
        using var datei = File.OpenRead(tarGz);
        using var gz = new GZipStream(datei, CompressionMode.Decompress);
        using var leser = new TarReader(gz);
        TarEntry? e;
        while ((e = leser.GetNextEntry()) is not null)
        {
            var name = e.Name; while (name.StartsWith("./", StringComparison.Ordinal)) name = name[2..];
            if (name.Length == 0 || name == ".") continue;
            var voll = Path.GetFullPath(Path.Combine(wurzel, name));
            if (!voll.StartsWith(wurzel, StringComparison.Ordinal)) throw new PaketException(Sprache.T("Ungültiger Pfad im Paket: {0}", e.Name));
            switch (e.EntryType)
            {
                case TarEntryType.Directory: Directory.CreateDirectory(voll); break;
                case TarEntryType.RegularFile or TarEntryType.V7RegularFile:
                    Directory.CreateDirectory(Path.GetDirectoryName(voll)!);
                    using (var aus = File.Create(voll)) e.DataStream?.CopyTo(aus);
                    anzahl++;
                    if (anzahl % 200 == 0) ausgabe?.Invoke(Sprache.T("  {0} Dateien …", anzahl));
                    break;
                default: throw new PaketException(Sprache.T("Das Paket enthält einen nicht erlaubten Eintrag ({0}): {1}", e.EntryType, e.Name));
            }
        }
        return anzahl;
    }
}
