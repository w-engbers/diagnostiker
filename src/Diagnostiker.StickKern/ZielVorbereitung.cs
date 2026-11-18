namespace Diagnostiker.StickKern;

/// <summary>
/// Die Sicherungen, die vor jedem Beschreiben eines Laufwerks gelten (für den Weg über das Docker-Skript und für den Weg über ein Paket):
/// Bestätigung durch den Menschen, erneute Prüfung, dass das Laufwerk noch das gezeigte ist, Sicherheitsregeln, Aushängen
/// und eine Nachprüfung nach dem Aushängen. Bei der kleinsten Abweichung wird abgebrochen, bevor etwas geschrieben wird.
/// </summary>
internal static class ZielVorbereitung
{
    /// <summary>Liefert das geprüfte, ausgehängte Laufwerk oder eine Fehlermeldung (dann wurde nichts verändert).</summary>
    public static async Task<(Laufwerk? Laufwerk, string? Fehler)> PruefenUndAushaengenAsync(
        ILaufwerkQuelle quelle, IBefehlsausfuehrer befehle, Laufwerk gewaehlt, string? eingabe, Action<string> ausgabe, CancellationToken ct)
    {
        // 1. Der Mensch muss den Gerätenamen genau eintippen.
        if (!Bestaetigung.Stimmt(eingabe, gewaehlt))
            return (null, Sprache.T("Bestätigung stimmt nicht, es wurde nichts verändert."));

        // 2. Das Laufwerk muss noch genau das sein, was gezeigt wurde.
        var aktuell = await Suchen(quelle, gewaehlt.Pfad, ct);
        if (aktuell is null) return (null, Sprache.T("{0} ist nicht mehr vorhanden, es wurde nichts verändert.", gewaehlt.Pfad));
        if (!Gleich(gewaehlt, aktuell))
            return (null, Sprache.T("{0} hat sich seit der Auswahl verändert (Größe, Modell oder Aufbau), es wurde nichts verändert.", gewaehlt.Pfad));

        // 3. Die Sicherheitsregeln gelten noch.
        var pruefung = Sicherheit.Pruefe(aktuell);
        if (!pruefung.Erlaubt) return (null, Sprache.T("{0} ist kein geeignetes Ziel: {1}", gewaehlt.Pfad, string.Join(" ", pruefung.Gruende)));

        // 4. Eingehängte Partitionen aushängen (ohne Root über udisks).
        foreach (var teil in aktuell.Partitionen.Where(p => p.Einhaengepunkte.Count > 0))
        {
            ausgabe(Sprache.T("Aushängen: {0}", teil.Pfad));
            var code = await befehle.AusfuehrenAsync("udisksctl", ["unmount", "-b", teil.Pfad], null, ausgabe, ct);
            if (code != 0) return (null, Sprache.T("{0} lässt sich nicht aushängen, es wurde nichts verändert.", teil.Pfad));
        }

        // 5. Nach dem Aushängen noch einmal prüfen: nichts darf mehr eingehängt sein, das Laufwerk muss unverändert sein.
        var danach = await Suchen(quelle, gewaehlt.Pfad, ct);
        if (danach is null) return (null, Sprache.T("{0} ist verschwunden, es wurde nichts verändert.", gewaehlt.Pfad));
        if (danach.AlleEinhaengepunkte.Any()) return (null, Sprache.T("{0} ist noch eingehängt, es wurde nichts verändert.", gewaehlt.Pfad));
        if (danach.Groesse != gewaehlt.Groesse || !Sicherheit.Pruefe(danach).Erlaubt)
            return (null, Sprache.T("{0} hat sich verändert, es wurde nichts verändert.", gewaehlt.Pfad));
        return (danach, null);
    }

    public static async Task<Laufwerk?> Suchen(ILaufwerkQuelle quelle, string pfad, CancellationToken ct) =>
        (await quelle.LesenAsync(ct)).FirstOrDefault(l => l.Pfad == pfad);

    static bool Gleich(Laufwerk a, Laufwerk b) =>
        a.Pfad == b.Pfad && a.Groesse == b.Groesse && a.Modell == b.Modell && a.Hersteller == b.Hersteller
        && a.Partitionen.Count == b.Partitionen.Count;
}
