using System.Text.Json;

namespace Diagnostiker.StickKern;

/// <summary>Liest die JSON-Ausgabe von <c>lsblk -J -b</c>. Tolerant gegenüber verschiedenen util-linux-Versionen
/// (Zahlen oder Zeichenketten für Wahrheitswerte, "mountpoint" oder "mountpoints").</summary>
public static class LsblkParser
{
    public const string Spalten = "NAME,PATH,SIZE,MODEL,VENDOR,TRAN,RM,HOTPLUG,TYPE,RO,MOUNTPOINTS,LABEL,FSTYPE";

    public static IReadOnlyList<Laufwerk> Lesen(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var ergebnis = new List<Laufwerk>();
        if (!doc.RootElement.TryGetProperty("blockdevices", out var geraete)) return ergebnis;

        foreach (var g in geraete.EnumerateArray())
        {
            if (Text(g, "type") != "disk") continue;   // keine Loop-Geräte, keine optischen Laufwerke

            var teile = new List<Partition>();
            Sammeln(g, teile);

            var pfad = Text(g, "path") ?? "/dev/" + Text(g, "name");
            ergebnis.Add(new Laufwerk(
                pfad, Text(g, "name") ?? pfad, Text(g, "model"), Text(g, "vendor"),
                Zahl(g, "size"), Text(g, "tran"),
                Wahr(g, "rm") || Wahr(g, "hotplug"), Wahr(g, "ro"),
                teile, Einhaenge(g)));
        }
        return ergebnis;
    }

    static void Sammeln(JsonElement eltern, List<Partition> ziel)
    {
        if (!eltern.TryGetProperty("children", out var kinder)) return;
        foreach (var k in kinder.EnumerateArray())
        {
            ziel.Add(new Partition(
                Text(k, "path") ?? "/dev/" + Text(k, "name"), Text(k, "label"), Text(k, "fstype"),
                Zahl(k, "size"), Einhaenge(k)));
            Sammeln(k, ziel);   // verschlüsselte oder logische Geräte darauf
        }
    }

    static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static long Zahl(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetInt64(),
            JsonValueKind.String when long.TryParse(v.GetString(), out var n) => n,
            _ => 0,
        };
    }

    static bool Wahr(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return false;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.Number => v.GetInt64() != 0,
            JsonValueKind.String => v.GetString() is "1" or "true",
            _ => false,
        };
    }

    static IReadOnlyList<string> Einhaenge(JsonElement e)
    {
        var liste = new List<string>();
        if (e.TryGetProperty("mountpoints", out var mp) && mp.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in mp.EnumerateArray())
                if (m.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(m.GetString())) liste.Add(m.GetString()!);
        }
        else if (Text(e, "mountpoint") is { Length: > 0 } alt) liste.Add(alt);
        return liste;
    }
}
