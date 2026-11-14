using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Diagnostiker.Kern;

public sealed record ProzessErgebnis(int? ExitCode, string Ausgabe, bool Timeout, string? Startfehler);

public static class ProzessAusfuehrer
{
    static ProzessAusfuehrer() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static async Task<ProzessErgebnis> AusfuehrenAsync(
        string programm, IEnumerable<string> argumente, TimeSpan timeout, string kodierung, CancellationToken ct = default,
        IReadOnlyDictionary<string, string>? umgebung = null)
    {
        var enc = KodierungWaehlen(kodierung);
        var psi = new ProcessStartInfo(programm)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = enc,
            StandardErrorEncoding = enc,
        };
        foreach (var a in argumente) psi.ArgumentList.Add(a);

        if (umgebung is not null) foreach (var (k, v) in umgebung) psi.Environment[k] = v;

        // Auf Linux: feste Sprache, damit die Regeln stabil gegen englische Ausgaben prüfen können.
        if (Plattform.IstLinux)
        {
            psi.Environment["LC_ALL"] = "C";
            psi.Environment["LANG"] = "C";
        }

        using var p = new Process { StartInfo = psi };
        var sb = new StringBuilder();
        var sperre = new object();
        void Anhaengen(string? zeile) { if (zeile is null) return; lock (sperre) sb.AppendLine(zeile); }
        p.OutputDataReceived += (_, e) => Anhaengen(e.Data);
        p.ErrorDataReceived += (_, e) => Anhaengen(e.Data);

        try
        {
            p.Start();
        }
        catch (Exception e)
        {
            return new ProzessErgebnis(null, "", false, e.Message);
        }

        p.StandardInput.Close(); // Kein Programm soll auf Eingaben warten.
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await p.WaitForExitAsync(cts.Token);
            p.WaitForExit(); // stellt sicher, dass die asynchronen Ausgabeströme leergelesen sind
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { /* bereits beendet */ }
            string teil; lock (sperre) teil = sb.ToString();
            if (ct.IsCancellationRequested) throw;
            return new ProzessErgebnis(null, teil, true, null);
        }

        string text; lock (sperre) text = sb.ToString();
        return new ProzessErgebnis(p.ExitCode, text, false, null);
    }

    private static Encoding KodierungWaehlen(string kodierung)
    {
        if (kodierung.Equals("utf8", StringComparison.OrdinalIgnoreCase) || !Plattform.IstWindows)
            return new UTF8Encoding(false);
        try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
        catch { return Encoding.UTF8; }
    }
}
