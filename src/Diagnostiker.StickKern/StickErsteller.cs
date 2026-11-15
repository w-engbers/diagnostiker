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
        var (_, fehler) = await ZielVorbereitung.PruefenUndAushaengenAsync(quelle, befehle, gewaehlt, eingabe, ausgabe, ct);
        if (fehler is not null) return Abbruch(fehler);

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

    async Task<Laufwerk?> Suchen(string pfad, CancellationToken ct) => await ZielVorbereitung.Suchen(quelle, pfad, ct);
}
