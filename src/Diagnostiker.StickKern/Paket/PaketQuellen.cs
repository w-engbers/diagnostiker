namespace Diagnostiker.StickKern.Paket;

/// <summary>Wählt die Quelle der Pakete: Standard ist das öffentliche GitHub-Repo, sonst ein Ordner oder eine https-Adresse.</summary>
public static class PaketQuellen
{
    public const string StandardRepo = "w-engbers/diagnostiker";

    public static IPaketQuelle Aus(string? angabe)
    {
        if (angabe is null) return HttpQuelle.GitHub(StandardRepo);
        if (angabe.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return new HttpQuelle(angabe);
        if (Directory.Exists(angabe)) return new OrdnerQuelle(Path.GetFullPath(angabe));
        throw new PaketException($"„{angabe}“ ist weder ein Ordner noch eine https-Adresse.");
    }
}
