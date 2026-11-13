namespace Diagnostiker.StickKern;

public sealed record ErstellErgebnis(bool Erfolg, string Meldung);

/// <summary>
/// Ablauf „Stick auf ein Laufwerk schreiben“ mit allen Sicherungen. Vor jedem Schritt, der etwas verändert, wird erneut geprüft;
/// bei der kleinsten Abweichung wird abgebrochen, bevor etwas geschrieben wird.
/// </summary>
public sealed class StickErsteller(ILaufwerkQuelle quelle, IBefehlsausfuehrer befehle, string projektOrdner)
{
    /// <summary>Wartezeit zwischen den Nachprüfungen (im Test auf null).</summary>
    public TimeSpan Pause { get; init; } = TimeSpan.FromSeconds(1);
    public int Nachpruefungen { get; init; } = 15;

    public async Task<ErstellErgebnis> ErstellenAsync(Laufwerk gewaehlt, string? eingabe, Action<string> ausgabe, CancellationToken ct = default)
    {
        // 1. Der Mensch muss den Gerätenamen genau eintippen.
        if (!Bestaetigung.Stimmt(eingabe, gewaehlt))
            return Abbruch("Bestätigung stimmt nicht, es wurde nichts verändert.");

        // 2. Das Laufwerk muss noch genau das sein, was gezeigt wurde.
        var aktuell = await Suchen(gewaehlt.Pfad, ct);
        if (aktuell is null) return Abbruch($"{gewaehlt.Pfad} ist nicht mehr vorhanden, es wurde nichts verändert.");
        if (!Gleich(gewaehlt, aktuell))
            return Abbruch($"{gewaehlt.Pfad} hat sich seit der Auswahl verändert (Größe, Modell oder Aufbau), es wurde nichts verändert.");

        // 3. Die Sicherheitsregeln gelten noch.
        var pruefung = Sicherheit.Pruefe(aktuell);
        if (!pruefung.Erlaubt) return Abbruch($"{gewaehlt.Pfad} ist kein geeignetes Ziel: {string.Join(" ", pruefung.Gruende)}");

        // 4. Eingehängte Partitionen aushängen (ohne Root über udisks).
        foreach (var teil in aktuell.Partitionen.Where(p => p.Einhaengepunkte.Count > 0))
        {
            ausgabe($"Aushängen: {teil.Pfad}");
            var code = await befehle.AusfuehrenAsync("udisksctl", ["unmount", "-b", teil.Pfad], null, ausgabe, ct);
            if (code != 0) return Abbruch($"{teil.Pfad} lässt sich nicht aushängen, es wurde nichts verändert.");
        }

        // 5. Nach dem Aushängen noch einmal prüfen: nichts darf mehr eingehängt sein, das Laufwerk muss unverändert sein.
        var danach = await Suchen(gewaehlt.Pfad, ct);
        if (danach is null) return Abbruch($"{gewaehlt.Pfad} ist verschwunden, es wurde nichts verändert.");
        if (danach.AlleEinhaengepunkte.Any()) return Abbruch($"{gewaehlt.Pfad} ist noch eingehängt, es wurde nichts verändert.");
        if (danach.Groesse != gewaehlt.Groesse || !Sicherheit.Pruefe(danach).Erlaubt)
            return Abbruch($"{gewaehlt.Pfad} hat sich verändert, es wurde nichts verändert.");

        // 6. Schreiben. Das Skript und der Container prüfen das Ziel noch einmal selbst.
        ausgabe($"Schreiben auf {gewaehlt.Pfad} …");
        var skript = Path.Combine(projektOrdner, "stick", "schreiben.sh");
        var umgebung = new Dictionary<string, string> { ["BESTAETIGT_LAUFWERK"] = gewaehlt.Pfad };
        var ergebnis = await befehle.AusfuehrenAsync(skript, [gewaehlt.Pfad], umgebung, ausgabe, ct);
        if (ergebnis != 0) return new ErstellErgebnis(false, $"Das Schreiben ist fehlgeschlagen (Exitcode {ergebnis}). Der Stick ist nicht brauchbar.");

        // 7. Nachprüfung: Beide Partitionen müssen mit den richtigen Bezeichnungen erscheinen.
        for (var i = 0; i < Nachpruefungen; i++)
        {
            var neu = await Suchen(gewaehlt.Pfad, ct);
            var namen = neu?.Partitionen.Select(p => p.Bezeichnung).ToHashSet() ?? [];
            if (namen.Contains("DIAGBOOT") && namen.Contains("DIAGDATEN"))
                return new ErstellErgebnis(true, "Der Stick ist fertig. Er kann jetzt abgezogen werden (vorher aushängen).");
            if (i < Nachpruefungen - 1 && Pause > TimeSpan.Zero) await Task.Delay(Pause, ct);
        }
        return new ErstellErgebnis(false, "Das Skript meldete Erfolg, aber die Partitionen DIAGBOOT und DIAGDATEN sind nicht zu sehen. Stick abziehen und neu einstecken, dann erneut ansehen.");
    }

    static ErstellErgebnis Abbruch(string meldung) => new(false, meldung);

    async Task<Laufwerk?> Suchen(string pfad, CancellationToken ct) =>
        (await quelle.LesenAsync(ct)).FirstOrDefault(l => l.Pfad == pfad);

    static bool Gleich(Laufwerk a, Laufwerk b) =>
        a.Pfad == b.Pfad && a.Groesse == b.Groesse && a.Modell == b.Modell && a.Hersteller == b.Hersteller
        && a.Partitionen.Count == b.Partitionen.Count;
}
