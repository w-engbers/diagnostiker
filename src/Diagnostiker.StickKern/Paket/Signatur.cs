using System.Security.Cryptography;

namespace Diagnostiker.StickKern.Paket;

/// <summary>
/// Signatur der Pakete: ECDSA mit der Kurve P-256 und SHA-256 (nur .NET-Standardbibliothek, keine Zusatzwerkzeuge).
/// Schlüssel und Signatur sind Base64: privat = PKCS#8, öffentlich = SubjectPublicKeyInfo, Signatur = r‖s (je 32 Bytes).
/// </summary>
public static class Signatur
{
    public static (string Privat, string Oeffentlich) SchluesselErzeugen()
    {
        using var k = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (Convert.ToBase64String(k.ExportPkcs8PrivateKey()), Convert.ToBase64String(k.ExportSubjectPublicKeyInfo()));
    }

    public static string Signieren(byte[] daten, string privat)
    {
        using var k = ECDsa.Create();
        k.ImportPkcs8PrivateKey(Convert.FromBase64String(privat.Trim()), out _);
        return Convert.ToBase64String(k.SignData(daten, HashAlgorithmName.SHA256));
    }

    public static bool Pruefen(byte[] daten, string signatur, string oeffentlich)
    {
        try
        {
            using var k = ECDsa.Create();
            k.ImportSubjectPublicKeyInfo(Convert.FromBase64String(oeffentlich.Trim()), out _);
            var sig = Convert.FromBase64String(signatur.Trim());
            return sig.Length == 64 && k.VerifyData(daten, sig, HashAlgorithmName.SHA256);
        }
        catch (Exception e) when (e is FormatException or CryptographicException or ArgumentException) { return false; }
    }
}

/// <summary>Die Schlüssel, denen das Frontend vertraut: der eingebaute und die über einen Schlüsselwechsel hinzugekommenen.</summary>
public sealed class Vertrauen(IEnumerable<string> schluessel)
{
    readonly List<string> _schluessel = schluessel.Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToList();

    public IReadOnlyList<string> Schluessel => _schluessel;

    /// <summary>Prüft Manifest und Signatur. Gibt das gelesene Manifest zurück; wirft <see cref="PaketException"/>, wenn die Signatur keinem
    /// vertrauten Schlüssel entspricht. Nennt ein gültig signiertes Manifest einen neuen Schlüssel, wird er <b>danach</b> vertraut
    /// (<c>NeuVertraut</c>), damit der Aufrufer ihn dauerhaft speichern kann.</summary>
    public (Manifest Manifest, string? NeuVertraut) Pruefen(byte[] manifestBytes, string signatur)
    {
        if (_schluessel.Count == 0) throw new PaketException("Es ist kein vertrauenswürdiger Schlüssel hinterlegt.");
        if (!_schluessel.Any(k => Signatur.Pruefen(manifestBytes, signatur, k)))
            throw new PaketException("Die Signatur des Pakets ist ungültig. Das Paket ist beschädigt oder stammt nicht vom Herausgeber. Es wird nicht benutzt.");
        var m = Manifest.Lesen(manifestBytes);
        string? neu = null;
        if (!string.IsNullOrWhiteSpace(m.NaechsterSchluessel) && !_schluessel.Contains(m.NaechsterSchluessel.Trim()))
        {
            neu = m.NaechsterSchluessel.Trim();
            try { using var k = ECDsa.Create(); k.ImportSubjectPublicKeyInfo(Convert.FromBase64String(neu), out _); }
            catch (Exception e) when (e is FormatException or CryptographicException or ArgumentException)
            { throw new PaketException("Das Manifest nennt einen ungültigen Folgeschlüssel."); }
            _schluessel.Add(neu);
        }
        return (m, neu);
    }
}
