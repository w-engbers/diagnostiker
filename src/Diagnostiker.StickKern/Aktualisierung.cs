using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Diagnostiker.StickKern;

public enum AenderungsArt { Neu, Geaendert, Entfernt }

public sealed record Aenderung(string Pfad, AenderungsArt Art, long Groesse);

public sealed record AktualisierungsPlan(
    string StickOrdner, string StagingOrdner, IReadOnlyList<Aenderung> Aenderungen, bool BasisVeraltet)
{
    public bool KeineAenderung => Aenderungen.Count == 0;
    public long BenoetigteBytes => Aenderungen.Where(a => a.Art != AenderungsArt.Entfernt).Sum(a => a.Groesse);
}

public sealed record AktualisierungsErgebnis(bool Erfolg, string Meldung);

/// <summary>
/// Vergleicht die Datenpartition eines Sticks mit dem frisch zusammengestellten Stand (Staging) und kopiert nur das Geänderte.
/// Der Ordner <c>protokolle/</c> wird nie angefasst. Gelöscht wird nur in <c>bin/</c> und <c>module/</c> (Dateien, die es im neuen Stand
/// nicht mehr gibt); eigene Dateien des Benutzers an anderer Stelle bleiben unberührt.
/// </summary>
public static class Aktualisierung
{
    static readonly string[] Geschuetzt = ["protokolle"];            // oberste Ordner, die nie berührt werden
    static readonly string[] Aufraeumbar = ["bin", "module"];         // nur dort werden überflüssige Dateien entfernt
    const string StickDatei = "stick.json";

    public static AktualisierungsPlan Planen(string stickOrdner, string stagingOrdner)
    {
        var aenderungen = new List<Aenderung>();
        var neu = Dateien(stagingOrdner);

        foreach (var rel in neu.Where(r => !IstGeschuetzt(r) && r != StickDatei).Order(StringComparer.Ordinal))
        {
            var quelle = Path.Combine(stagingOrdner, rel);
            var ziel = Path.Combine(stickOrdner, rel);
            var groesse = new FileInfo(quelle).Length;
            if (!File.Exists(ziel)) aenderungen.Add(new Aenderung(rel, AenderungsArt.Neu, groesse));
            else if (new FileInfo(ziel).Length != groesse || Hash(ziel) != Hash(quelle))
                aenderungen.Add(new Aenderung(rel, AenderungsArt.Geaendert, groesse));
        }

        var behalten = neu.ToHashSet();
        foreach (var rel in Dateien(stickOrdner).Where(r => Aufraeumbar.Any(a => r.StartsWith(a + "/")) && !behalten.Contains(r)).Order(StringComparer.Ordinal))
            aenderungen.Add(new Aenderung(rel, AenderungsArt.Entfernt, 0));

        return new AktualisierungsPlan(stickOrdner, stagingOrdner, aenderungen, BasisVeraltet(stickOrdner, stagingOrdner));
    }

    public static AktualisierungsErgebnis Anwenden(AktualisierungsPlan plan, Action<string>? fortschritt = null)
    {
        var stick = Path.GetFullPath(plan.StickOrdner);
        fortschritt ??= _ => { };

        // Vorbedingungen: beschreibbar, genug Platz
        try
        {
            var probe = Path.Combine(stick, ".schreibtest");
            File.WriteAllText(probe, "x");
            File.Delete(probe);
        }
        catch (Exception e) { return new(false, $"Der Stick ist nicht beschreibbar ({e.Message}). Es wurde nichts verändert."); }

        var frei = new DriveInfo(stick).AvailableFreeSpace;
        if (frei < plan.BenoetigteBytes + 16L * 1024 * 1024)
            return new(false, $"Zu wenig freier Platz auf dem Stick (frei {Sicherheit.Groesse(frei)}, nötig {Sicherheit.Groesse(plan.BenoetigteBytes)}). Es wurde nichts verändert.");

        try
        {
            foreach (var a in plan.Aenderungen.Where(a => a.Art != AenderungsArt.Entfernt))
            {
                var ziel = Sicher(stick, a.Pfad);
                Directory.CreateDirectory(Path.GetDirectoryName(ziel)!);
                var zwischen = ziel + ".neu";
                fortschritt($"Kopiere {a.Pfad}");
                File.Copy(Path.Combine(plan.StagingOrdner, a.Pfad), zwischen, overwrite: true);
                File.Move(zwischen, ziel, overwrite: true);
            }

            foreach (var a in plan.Aenderungen.Where(a => a.Art == AenderungsArt.Entfernt))
            {
                fortschritt($"Entferne {a.Pfad}");
                File.Delete(Sicher(stick, a.Pfad));
            }
            LeereOrdnerEntfernen(stick);

            // Kontrolle: Alles, was kopiert wurde, muss jetzt genau dem Staging entsprechen.
            foreach (var a in plan.Aenderungen.Where(a => a.Art != AenderungsArt.Entfernt))
            {
                if (Hash(Path.Combine(stick, a.Pfad)) != Hash(Path.Combine(plan.StagingOrdner, a.Pfad)))
                    return new(false, $"Kontrolle fehlgeschlagen: {a.Pfad} stimmt nach dem Kopieren nicht überein.");
            }

            StickDateiSchreiben(stick, plan.StagingOrdner);
        }
        catch (Exception e)
        {
            return new(false, $"Fehler beim Aktualisieren: {e.Message}");
        }

        return new(true, plan.KeineAenderung ? "Der Stick war schon auf dem neuesten Stand." : $"{plan.Aenderungen.Count} Änderung(en) übernommen und kontrolliert.");
    }

    /// <summary>stick.json des Sticks: Programm, Module und Werkzeuge vom neuen Stand, Basis und Erstelldatum bleiben die des Sticks
    /// (die Basis wird hier nicht aktualisiert, sie darf deshalb auch nicht als aktuell eingetragen werden).</summary>
    static void StickDateiSchreiben(string stick, string staging)
    {
        var neu = JsonNode.Parse(File.ReadAllText(Path.Combine(staging, StickDatei)))!.AsObject();
        var altDatei = Path.Combine(stick, StickDatei);
        if (File.Exists(altDatei) && JsonNode.Parse(File.ReadAllText(altDatei)) is JsonObject alt)
        {
            if (alt["teile"]?["basis"] is JsonNode basis) neu["teile"]!["basis"] = basis.DeepClone();
            if (alt["erstellt"] is JsonNode erstellt) neu["erstellt"] = erstellt.DeepClone();
        }
        neu["aktualisiert"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        var zwischen = altDatei + ".neu";
        File.WriteAllText(zwischen, neu.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        File.Move(zwischen, altDatei, overwrite: true);
    }

    static bool BasisVeraltet(string stick, string staging)
    {
        try
        {
            var a = JsonNode.Parse(File.ReadAllText(Path.Combine(stick, StickDatei)))?["teile"]?["basis"]?["sha256"]?.GetValue<string>();
            var b = JsonNode.Parse(File.ReadAllText(Path.Combine(staging, StickDatei)))?["teile"]?["basis"]?["sha256"]?.GetValue<string>();
            return a is not null && b is not null && a != b;
        }
        catch { return false; }
    }

    static bool IstGeschuetzt(string rel) => Geschuetzt.Any(g => rel == g || rel.StartsWith(g + "/"));

    static string Sicher(string stick, string rel)
    {
        var voll = Path.GetFullPath(Path.Combine(stick, rel));
        if (!voll.StartsWith(stick + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidOperationException($"Pfad außerhalb des Sticks: {rel}");
        return voll;
    }

    static List<string> Dateien(string wurzel) =>
        Directory.Exists(wurzel)
            ? Directory.EnumerateFiles(wurzel, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(wurzel, f).Replace(Path.DirectorySeparatorChar, '/'))
                .Where(r => !r.EndsWith(".neu")).ToList()
            : [];

    static void LeereOrdnerEntfernen(string stick)
    {
        foreach (var oben in Aufraeumbar)
        {
            var wurzel = Path.Combine(stick, oben);
            if (!Directory.Exists(wurzel)) continue;
            foreach (var d in Directory.EnumerateDirectories(wurzel, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length))
                if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d);
        }
    }

    /// <summary>Kann in dem Ordner eine Datei angelegt werden? (Ein schreibgeschützt eingehängter Stick besteht das nicht.)</summary>
    public static bool IstBeschreibbar(string ordner)
    {
        try
        {
            var probe = Path.Combine(ordner, ".schreibtest");
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    public static string Hash(string datei)
    {
        using var s = File.OpenRead(datei);
        return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
    }
}
