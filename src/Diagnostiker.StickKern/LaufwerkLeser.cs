using System.Diagnostics;

namespace Diagnostiker.StickKern;

/// <summary>Fragt lsblk nach den Laufwerken des Rechners (nur lesend).</summary>
public static class LaufwerkLeser
{
    public static async Task<IReadOnlyList<Laufwerk>> LesenAsync(CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo("lsblk")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        foreach (var a in new[] { "-J", "-b", "-e7", "-o", LsblkParser.Spalten }) psi.ArgumentList.Add(a);
        psi.Environment["LC_ALL"] = "C";

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("lsblk lässt sich nicht starten.");
        var ausgabe = await p.StandardOutput.ReadToEndAsync(ct);
        var fehler = await p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0) throw new InvalidOperationException($"lsblk fehlgeschlagen: {fehler.Trim()}");
        return LsblkParser.Lesen(ausgabe);
    }
}
