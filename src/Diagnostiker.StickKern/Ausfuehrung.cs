using System.Diagnostics;

namespace Diagnostiker.StickKern;

/// <summary>Quelle der Laufwerksliste (echt: lsblk, im Test: Attrappe).</summary>
public interface ILaufwerkQuelle
{
    Task<IReadOnlyList<Laufwerk>> LesenAsync(CancellationToken ct);
}

/// <summary>Führt externe Programme aus (echt: Prozess, im Test: Attrappe). Liefert den Exitcode.</summary>
public interface IBefehlsausfuehrer
{
    Task<int> AusfuehrenAsync(string programm, IReadOnlyList<string> argumente,
        IReadOnlyDictionary<string, string>? umgebung, Action<string> zeile, CancellationToken ct);
}

public sealed class LsblkQuelle : ILaufwerkQuelle
{
    public Task<IReadOnlyList<Laufwerk>> LesenAsync(CancellationToken ct) => LaufwerkLeser.LesenAsync(ct);
}

public sealed class ProzessBefehle : IBefehlsausfuehrer
{
    public async Task<int> AusfuehrenAsync(string programm, IReadOnlyList<string> argumente,
        IReadOnlyDictionary<string, string>? umgebung, Action<string> zeile, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(programm)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false,
        };
        foreach (var a in argumente) psi.ArgumentList.Add(a);
        if (umgebung is not null) foreach (var (k, v) in umgebung) psi.Environment[k] = v;

        using var p = new Process { StartInfo = psi };
        var sperre = new object();
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (sperre) zeile(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (sperre) zeile(e.Data); };
        p.Start();
        p.StandardInput.Close();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        await p.WaitForExitAsync(ct);
        return p.ExitCode;
    }
}
