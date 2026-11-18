using System.Security.Cryptography;

namespace Diagnostiker.StickKern.Paket;

public static class PaketPruefer
{
    public static string Sha256(string datei, Action<long>? fortschritt = null)
    {
        using var s = File.OpenRead(datei);
        using var h = SHA256.Create();
        var puffer = new byte[1 << 20]; long summe = 0; int n;
        while ((n = s.Read(puffer, 0, puffer.Length)) > 0) { h.TransformBlock(puffer, 0, n, null, 0); summe += n; fortschritt?.Invoke(summe); }
        h.TransformFinalBlock([], 0, 0);
        return Convert.ToHexStringLower(h.Hash!);
    }

    /// <summary>Prüft Größe und SHA-256 einer heruntergeladenen Datei gegen das (bereits geprüfte) Manifest.</summary>
    public static void DateiPruefen(string datei, PaketDatei soll, Action<long>? fortschritt = null)
    {
        if (!File.Exists(datei)) throw new PaketException(Sprache.T("{0} fehlt.", soll.Name));
        var groesse = new FileInfo(datei).Length;
        if (groesse != soll.Groesse) throw new PaketException(Sprache.T("{0} hat die falsche Größe ({1} statt {2} Bytes).", soll.Name, groesse, soll.Groesse));
        if (!string.Equals(Sha256(datei, fortschritt), soll.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new PaketException(Sprache.T("{0}: Die Prüfsumme stimmt nicht. Die Datei ist beschädigt und wird nicht benutzt.", soll.Name));
    }
}
