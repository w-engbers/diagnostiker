namespace Diagnostiker.StickKern;

public sealed record Vorbereitung(AktualisierungsPlan? Plan, string? Fehler, string? PartitionPfad, string? LaufwerkPfad = null, string? BootPartitionPfad = null);

sealed record Fund(Laufwerk Laufwerk, Partition Teil);

/// <summary>
/// Ablauf „Stick aktualisieren“: Stand zusammenstellen, den Stick finden (USB, Bezeichnung DIAGDATEN, wirklich ein
/// Diagnostiker-Stick), bei Bedarf einhängen, Plan berechnen. Angewendet wird erst nach der Bestätigung durch den Menschen.
/// </summary>
public sealed class StickAktualisierer(ILaufwerkQuelle quelle, IBefehlsausfuehrer befehle, string projektOrdner)
{
    public string Staging => Path.Combine(projektOrdner, "build", "stick-staging");

    /// <summary>Das gebaute Live-System, mit dem das Live-System (Basis) des Sticks ersetzt wird.</summary>
    public string Iso => Path.Combine(projektOrdner, "build", "out", "diagnostiker.iso");

    /// <summary>Nur für Tests: andere erlaubte Einhängeorte (Vorgabe: /media, /run/media, /mnt).</summary>
    public IReadOnlyList<string>? ErlaubteOrte { get; init; }

    public async Task<Vorbereitung> VorbereitenAsync(Action<string> ausgabe, CancellationToken ct = default)
    {
        ausgabe("Stand zusammenstellen …");
        var code = await befehle.AusfuehrenAsync(Path.Combine(projektOrdner, "stick", "staging.sh"), [], null, ausgabe, ct);
        if (code != 0) return new(null, $"Der neue Stand lässt sich nicht zusammenstellen (Exitcode {code}).", null);

        var partition = await SuchenAsync(ct);
        if (partition.Fehler is not null) return new(null, partition.Fehler, null);
        var laufwerk = partition.Treffer!.Laufwerk;
        var teil = partition.Treffer.Teil;

        var punkt = teil.Einhaengepunkte.FirstOrDefault();
        if (punkt is not null && !Aktualisierung.IstBeschreibbar(punkt))
        {
            // z. B. nur zum Lesen eingehängt: aushängen und beschreibbar neu einhängen
            ausgabe($"{teil.Pfad} ist schreibgeschützt eingehängt, hänge ihn neu ein.");
            var u = await befehle.AusfuehrenAsync("udisksctl", ["unmount", "-b", teil.Pfad], null, ausgabe, ct);
            if (u != 0) return new(null, $"{teil.Pfad} ist schreibgeschützt eingehängt und lässt sich nicht aushängen.", null);
            punkt = null;
        }
        if (punkt is null)
        {
            ausgabe($"Einhängen: {teil.Pfad}");
            var c = await befehle.AusfuehrenAsync("udisksctl", ["mount", "-b", teil.Pfad], null, ausgabe, ct);
            if (c != 0) return new(null, $"{teil.Pfad} lässt sich nicht einhängen.", null);
            punkt = (await quelle.LesenAsync(ct)).FirstOrDefault(l => l.Pfad == laufwerk.Pfad)?
                .Partitionen.FirstOrDefault(p => p.Pfad == teil.Pfad)?.Einhaengepunkte.FirstOrDefault();
            if (punkt is null) return new(null, $"{teil.Pfad} ist nach dem Einhängen nicht zu finden.", null);
            if (!Aktualisierung.IstBeschreibbar(punkt))
                return new(null, $"{teil.Pfad} lässt sich nicht beschreibbar einhängen (Schreibschutzschalter am Stick?).", teil.Pfad);
        }

        if (!File.Exists(Path.Combine(punkt, "stick.json")))
            return new(null, $"Auf {teil.Pfad} liegt keine stick.json: Das ist kein Diagnostiker-Stick (oder ein sehr alter). Zum Erstellen den Menüpunkt „Stick erstellen“ benutzen.", null);

        var boot = laufwerk.Partitionen.FirstOrDefault(p => p.Bezeichnung == "DIAGBOOT");
        return new(Aktualisierung.Planen(punkt, Staging), null, teil.Pfad, laufwerk.Pfad, boot?.Pfad);
    }

    /// <summary>Hängt den Stick wieder aus (nach Abbruch oder reiner Planansicht), ohne etwas geschrieben zu haben.</summary>
    public async Task FreigebenAsync(Vorbereitung v, Action<string> ausgabe, CancellationToken ct = default)
    {
        if (v.PartitionPfad is null) return;
        await befehle.AusfuehrenAsync("udisksctl", ["unmount", "-b", v.PartitionPfad], null, ausgabe, ct);
    }

    /// <summary>Wendet den Plan an. Mit <paramref name="basis"/> wird danach auch das Live-System (Partition DIAGBOOT) ersetzt.</summary>
    public async Task<AktualisierungsErgebnis> AnwendenAsync(Vorbereitung v, Action<string> ausgabe, bool basis = false, CancellationToken ct = default)
    {
        if (v.Plan is null) return new(false, v.Fehler ?? "Kein Plan.");
        var ergebnis = Aktualisierung.Anwenden(v.Plan, ausgabe);
        var zusatz = "";

        if (ergebnis.Erfolg && basis)
        {
            var b = await BasisErsetzenAsync(v, ausgabe, ct);
            if (!b.Erfolg)
                ergebnis = new(false, $"Programm und Module sind aktualisiert, das Live-System aber nicht: {b.Meldung}");
            else zusatz = " " + b.Meldung;
        }

        // Zurückschreiben erzwingen und aushängen, damit der Stick abgezogen werden kann.
        if (v.PartitionPfad is not null)
        {
            await befehle.AusfuehrenAsync("sync", [], null, ausgabe, ct);
            await befehle.AusfuehrenAsync("udisksctl", ["unmount", "-b", v.PartitionPfad], null, ausgabe, ct);
        }
        return ergebnis.Erfolg ? ergebnis with { Meldung = ergebnis.Meldung + zusatz + " Der Stick ist ausgehängt und kann abgezogen werden." } : ergebnis;
    }

    async Task<AktualisierungsErgebnis> BasisErsetzenAsync(Vorbereitung v, Action<string> ausgabe, CancellationToken ct)
    {
        if (v.LaufwerkPfad is null || v.BootPartitionPfad is null)
            return new(false, "Auf dem Stick gibt es keine Partition DIAGBOOT.");
        if (!File.Exists(Iso)) return new(false, "Es gibt kein Live-Abbild (zuerst „Abbild (ISO) bauen“).");

        // Das Laufwerk neu lesen: Es muss noch dasselbe, zulässige Laufwerk sein, und DIAGBOOT darf nicht eingehängt bleiben.
        var jetzt = (await quelle.LesenAsync(ct)).FirstOrDefault(l => l.Pfad == v.LaufwerkPfad);
        if (jetzt is null) return new(false, $"{v.LaufwerkPfad} ist verschwunden.");
        var pruefung = Sicherheit.Pruefe(jetzt, erlaubteOrte: ErlaubteOrte);
        if (!pruefung.Erlaubt) return new(false, $"{v.LaufwerkPfad} ist nicht mehr als Ziel zulässig: {string.Join(" ", pruefung.Gruende)}");
        var boot = jetzt.Partitionen.FirstOrDefault(p => p.Pfad == v.BootPartitionPfad && p.Bezeichnung == "DIAGBOOT");
        if (boot is null) return new(false, "Die Partition DIAGBOOT hat sich verändert.");
        if (boot.Einhaengepunkte.Count > 0)
        {
            ausgabe($"Aushängen: {boot.Pfad}");
            var u = await befehle.AusfuehrenAsync("udisksctl", ["unmount", "-b", boot.Pfad], null, ausgabe, ct);
            if (u != 0) return new(false, $"{boot.Pfad} lässt sich nicht aushängen.");
        }

        ausgabe("Live-System aktualisieren (das dauert einige Minuten, bitte nicht abziehen) …");
        var skript = Path.Combine(projektOrdner, "stick", "basis-schreiben.sh");
        var umgebung = new Dictionary<string, string> { ["BESTAETIGT_LAUFWERK"] = v.LaufwerkPfad };
        var code = await befehle.AusfuehrenAsync(skript, [v.LaufwerkPfad], umgebung, ausgabe, ct);
        if (code != 0) return new(false, $"Das Schreiben des Live-Systems ist fehlgeschlagen (Exitcode {code}). Den Vorgang wiederholen; bis dahin ist der Stick eventuell nicht startfähig.");

        Aktualisierung.BasisUebernehmen(v.Plan!.StickOrdner, Staging);
        return new(true, "Das Live-System ist aktualisiert.");
    }

    async Task<(Fund? Treffer, string? Fehler)> SuchenAsync(CancellationToken ct)
    {
        var treffer = new List<Fund>();
        foreach (var l in await quelle.LesenAsync(ct))
            foreach (var p in l.Partitionen.Where(p => p.Bezeichnung == "DIAGDATEN")) treffer.Add(new Fund(l, p));

        if (treffer.Count == 0) return (null, "Kein Diagnostiker-Stick gefunden (keine Partition DIAGDATEN). Stick einstecken und erneut versuchen.");
        if (treffer.Count > 1) return (null, "Mehrere Partitionen mit der Bezeichnung DIAGDATEN gefunden. Bitte nur einen Stick einstecken.");

        var laufwerk = treffer[0].Laufwerk;
        var pruefung = Sicherheit.Pruefe(laufwerk, erlaubteOrte: ErlaubteOrte);
        if (!pruefung.Erlaubt) return (null, $"{laufwerk.Pfad} ist als Ziel nicht zulässig: {string.Join(" ", pruefung.Gruende)}");
        return (treffer[0], null);
    }
}
