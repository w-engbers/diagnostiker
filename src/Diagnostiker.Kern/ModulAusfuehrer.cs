using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Diagnostiker.Kern;

public sealed class ModulAusfuehrer(string stickWurzel)
{
    /// <summary>Verzeichnis der mitgelieferten Tools für das laufende System.</summary>
    public string WerkzeugVerzeichnis { get; } = Path.Combine(stickWurzel, "werkzeuge", Plattform.WerkzeugOrdner);

    /// <summary>Ordner der Prüfmodule auf dem Stick (Platzhalter <c>{module}</c>): längere Skripte liegen als Datei neben dem Manifest.</summary>
    public string ModulVerzeichnis { get; } = Path.Combine(stickWurzel, "module");

    public bool IstAnwendbar(ModulDefinition m) =>
        m.Schritte.Any(s => OsPasst(s));

    private static bool OsPasst(SchrittDefinition s) =>
        string.IsNullOrEmpty(s.Os) || s.Os.Equals(Plattform.OsName, StringComparison.OrdinalIgnoreCase);

    public async Task<ModulErgebnis> AusfuehrenAsync(
        ModulDefinition modul, IReadOnlyDictionary<string, string> parameter, CancellationToken ct = default)
    {
        var erg = new ModulErgebnis { Id = modul.Id, Titel = modul.Titel, Beginn = DateTime.Now };
        foreach (var schritt in modul.Schritte.Where(OsPasst))
        {
            erg.Schritte.Add(await SchrittAusfuehrenAsync(schritt, parameter, modul.Parameter.Where(p => p.Geheim).Select(p => p.Name).ToHashSet(), ct));
        }

        erg.Status = ZusammenfassenStatus(erg.Schritte.Select(s => s.Status));
        return erg;
    }

    /// <summary>Schlechtester Einzelstatus gewinnt; "Übersprungen"/"Info" allein zählen nicht als OK.</summary>
    public static Status ZusammenfassenStatus(IEnumerable<Status> stati)
    {
        var liste = stati.ToList();
        if (liste.Count == 0) return Status.Uebersprungen;
        var schlimmster = liste.Max();
        if (schlimmster >= Status.Warnung) return schlimmster;
        return liste.Contains(Status.Ok) ? Status.Ok : schlimmster;
    }

    private async Task<SchrittErgebnis> SchrittAusfuehrenAsync(
        SchrittDefinition s, IReadOnlyDictionary<string, string> parameter, ISet<string> geheim, CancellationToken ct)
    {
        var res = new SchrittErgebnis { Titel = s.Titel };

        string Ersetzen(string text)
        {
            text = text.Replace("{werkzeuge}", WerkzeugVerzeichnis).Replace("{module}", ModulVerzeichnis);
            foreach (var (k, v) in parameter) if (!geheim.Contains(k)) text = text.Replace("{" + k + "}", v);
            return text;
        }

        var programm = Ersetzen(s.Befehl);
        var argumente = s.Argumente.Select(Ersetzen).ToList();
        res.Befehlszeile = programm + (argumente.Count > 0 ? " " + string.Join(' ', argumente) : "");

        if (s.Admin && !Plattform.HatAdminRechte)
        {
            res.Status = Status.Uebersprungen;
            res.Meldungen.Add("Benötigt Administrator-/Root-Rechte – Programm entsprechend neu starten.");
            return res;
        }

        var sw = Stopwatch.StartNew();
        var pr = await ProzessAusfuehrer.AusfuehrenAsync(
            programm, argumente, TimeSpan.FromSeconds(s.TimeoutSekunden), s.Kodierung, ct,
            geheim.ToDictionary(n => "DIAGNOSTIKER_PARAM_" + n.ToUpperInvariant(), n => parameter.GetValueOrDefault(n, "")));
        res.Sekunden = Math.Round(sw.Elapsed.TotalSeconds, 1);
        res.ExitCode = pr.ExitCode;
        res.Ausgabe = pr.Ausgabe;

        if (pr.Startfehler is not null)
        {
            res.Status = Status.Uebersprungen;
            res.Meldungen.Add($"Programm nicht startbar: {pr.Startfehler}");
            if (s.Hinweis.Length > 0) res.Meldungen.Add(s.Hinweis);
            return res;
        }

        if (pr.Timeout)
        {
            res.Status = Status.Warnung;
            res.Meldungen.Add($"Zeitüberschreitung nach {s.TimeoutSekunden} s – Schritt abgebrochen.");
            return res;
        }

        Bewerten(s, res);
        return res;
    }

    /// <summary>Wendet alle Regeln an; jede zutreffende Regel liefert eine Meldung, der schlimmste Status gilt.</summary>
    public static void Bewerten(SchrittDefinition s, SchrittErgebnis res)
    {
        var stati = new List<Status>();
        if (s.ZeigeAusgabe)
            res.Meldungen.AddRange(res.Ausgabe.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(z => !IstMarkierung(z)));   // reine Markierungen (KEIN_ADAPTER_BT) sind nur für die Regeln da
        foreach (var r in s.Regeln)
        {
            if (!Trifft(r, res)) continue;
            stati.Add(r.Status);
            if (r.Meldung.Length > 0) res.Meldungen.Add(r.Meldung);
        }

        res.Status = stati.Count == 0 ? Status.Info : ZusammenfassenStatus(stati);
    }

    /// <summary>Eine Zeile aus Großbuchstaben, Ziffern und mindestens einem Unterstrich ist eine Markierung für die Regeln, kein Text für Menschen.</summary>
    public static bool IstMarkierung(string zeile) => MarkierungsMuster.IsMatch(zeile);
    private static readonly Regex MarkierungsMuster = new(@"^[A-Z][A-Z0-9]*(_[A-Z0-9]+)+$", RegexOptions.Compiled);

    private static bool Trifft(Regel r, SchrittErgebnis res) => r.Typ.ToLowerInvariant() switch
    {
        "enthaelt" => res.Ausgabe.Contains(r.Wert, StringComparison.OrdinalIgnoreCase),
        "regex" => Regex.IsMatch(res.Ausgabe, r.Wert, RegexOptions.IgnoreCase | RegexOptions.Multiline, TimeSpan.FromSeconds(2)),
        "exitcode" => res.ExitCode?.ToString() == r.Wert,
        "exitcodenicht" => res.ExitCode is not null && res.ExitCode.ToString() != r.Wert,
        _ => false,
    };
}
