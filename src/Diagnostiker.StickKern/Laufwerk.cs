namespace Diagnostiker.StickKern;

/// <summary>Eine Partition (oder ein darauf liegendes Gerät) eines Laufwerks.</summary>
public sealed record Partition(
    string Pfad, string? Bezeichnung, string? Dateisystem, long Groesse, IReadOnlyList<string> Einhaengepunkte);

/// <summary>Ein ganzes Laufwerk, wie es lsblk meldet.</summary>
public sealed record Laufwerk(
    string Pfad,
    string Name,
    string? Modell,
    string? Hersteller,
    long Groesse,
    string? Transport,
    bool Wechselmedium,
    bool SchreibGeschuetzt,
    IReadOnlyList<Partition> Partitionen,
    IReadOnlyList<string> Einhaengepunkte)
{
    /// <summary>Alle Einhängepunkte des Laufwerks und seiner Partitionen.</summary>
    public IEnumerable<string> AlleEinhaengepunkte =>
        Einhaengepunkte.Concat(Partitionen.SelectMany(p => p.Einhaengepunkte));

    public string Beschreibung
    {
        get
        {
            var name = string.Join(' ', new[] { Hersteller?.Trim(), Modell?.Trim() }.Where(s => !string.IsNullOrEmpty(s)));
            return string.IsNullOrEmpty(name) ? Sprache.T("(ohne Modellangabe)") : name;
        }
    }
}
