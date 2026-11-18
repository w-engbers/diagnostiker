using System.Runtime.InteropServices;

namespace Diagnostiker.StickKern;

/// <summary>
/// Protokoll eines Erstellungslaufs (Datei <c>~/.cache/diagnostiker/letzter-lauf.log</c>, wird je Lauf neu angelegt).
/// Hält auch fest, wenn das Programm von außen beendet wird (Terminal geschlossen, Signal): so bleibt ein Abbruch nicht stumm.
/// </summary>
public sealed class LaufProtokoll : IDisposable
{
    readonly StreamWriter _datei;
    readonly List<PosixSignalRegistration> _signale = [];

    public string Pfad { get; }

    public LaufProtokoll(string? pfad = null)
    {
        Pfad = Path.GetFullPath(pfad ?? Path.Combine(Paket.PaketHolen.StandardZwischenspeicher(), "..", "letzter-lauf.log"));
        Directory.CreateDirectory(Path.GetDirectoryName(Pfad)!);
        _datei = new StreamWriter(Pfad, false) { AutoFlush = true };
        if (OperatingSystem.IsLinux())
            foreach (var s in new[] { PosixSignal.SIGHUP, PosixSignal.SIGTERM, PosixSignal.SIGINT, PosixSignal.SIGQUIT })
                _signale.Add(PosixSignalRegistration.Create(s, _ => Schreiben($"SIGNAL {s} empfangen (Programm wird beendet)")));
    }

    public void Schreiben(string zeile)
    {
        lock (_datei) _datei.WriteLine($"{DateTime.Now:HH:mm:ss} {zeile}");
    }

    public void Dispose()
    {
        foreach (var s in _signale) s.Dispose();
        lock (_datei) _datei.Dispose();
    }
}
