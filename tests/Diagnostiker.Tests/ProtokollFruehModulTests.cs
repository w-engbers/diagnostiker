using System.Diagnostics;
using Diagnostiker.Kern;

namespace Diagnostiker.Tests;

/// <summary>Prüft das Modul „Protokoll früherer Starts“ mit einem nachgebauten Wurzelverzeichnis (DIAGNOSTIKER_PRUEFLING)
/// und einer Attrappe von journalctl. Das Einhängen echter Laufwerke ist hier nicht Teil des Tests.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class ProtokollFruehModulTests : IDisposable
{
    readonly string _ordner = Directory.CreateTempSubdirectory("fruehtest").FullName;
    string Wurzel => Path.Combine(_ordner, "wurzel");
    string Bin => Path.Combine(_ordner, "bin");

    public ProtokollFruehModulTests()
    {
        Directory.CreateDirectory(Bin);
        Directory.CreateDirectory(Path.Combine(Wurzel, "etc"));
        Directory.CreateDirectory(Path.Combine(Wurzel, "var", "log"));
        File.WriteAllText(Path.Combine(Wurzel, "etc", "os-release"), "PRETTY_NAME=\"Testlinux 1\"\n");
    }

    public void Dispose() => Directory.Delete(_ordner, true);

    void Datei(string pfad, string inhalt) => File.WriteAllText(pfad, inhalt);

    /// <summary>Attrappe: --list-boots liefert Start 0, Fehlerzeilen aus err0.txt, die letzten Zeilen aus ende0.txt.</summary>
    void Journal(string fehler, string ende)
    {
        Directory.CreateDirectory(Path.Combine(Wurzel, "var", "log", "journal"));
        Datei(Path.Combine(_ordner, "err0.txt"), fehler);
        Datei(Path.Combine(_ordner, "ende0.txt"), ende);
        var pfad = Path.Combine(Bin, "journalctl");
        Datei(pfad, $$"""
            #!/bin/sh
            boot=; prio=; n=
            while [ $# -gt 0 ]; do case "$1" in --list-boots) lb=1;; -b) boot=$2; shift;; -p) prio=$2; shift;; -n) n=$2; shift;; esac; shift; done
            if [ -n "$lb" ]; then echo " 0 abc123 Mon 2026-11-09 10:00:00 CET—Mon 2026-11-09 11:00:00 CET"; exit 0; fi
            [ "$boot" = 0 ] || exit 0
            if [ -n "$prio" ]; then cat {{_ordner}}/err0.txt; else cat {{_ordner}}/ende0.txt; fi
            """.Replace("\r", ""));
        File.SetUnixFileMode(pfad, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    SchrittErgebnis Lauf()
    {
        var (module, _) = ModulLader.Laden(ModulLader.ModulVerzeichnisFinden()!);
        var s = module.Single(m => m.Id == "system.protokoll-frueher").Schritte.Single(x => x.Os == "linux");
        var psi = new ProcessStartInfo("sh") { RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var a in s.Argumente) psi.ArgumentList.Add(a);
        psi.Environment["PATH"] = Bin + ":/usr/bin:/bin";
        psi.Environment["LC_ALL"] = "C";
        psi.Environment["DIAGNOSTIKER_PRUEFLING"] = Wurzel;
        using var p = Process.Start(psi)!;
        var aus = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        var res = new SchrittErgebnis { Ausgabe = aus, ExitCode = p.ExitCode };
        ModulAusfuehrer.Bewerten(s, res);
        return res;
    }

    [Fact]
    public void Sauberer_Start_mit_Herunterfahren_ist_in_Ordnung()
    {
        Journal("", "Nov 09 systemd[1]: Reached target Power-Off.\nNov 09 systemd-journald: Journal stopped");
        var r = Lauf();
        Assert.Equal(Status.Ok, r.Status);
        Assert.Contains("Testlinux 1", r.Ausgabe);
    }

    [Fact]
    public void Ende_ohne_Herunterfahren_ist_eine_Warnung()
    {
        Journal("", "Nov 09 irgendwas laeuft");
        var r = Lauf();
        Assert.Equal(Status.Warnung, r.Status);
        Assert.Contains("Unerwartetes Ausschalten", r.Ausgabe);
    }

    [Fact]
    public void Datentraegerfehler_im_frueheren_Start_sind_ein_Fehler()
    {
        Journal("kernel: Buffer I/O error on dev sda1, logical block 4\nkernel: Kernel panic - not syncing", "Journal stopped");
        var r = Lauf();
        Assert.Equal(Status.Fehler, r.Status);
        Assert.Contains("Datenträger-Fehler im Protokoll: 1 ", r.Ausgabe);
        Assert.Contains("Kernel-Absturz", r.Ausgabe);
    }

    [Fact]
    public void Klassisches_Syslog_wird_als_Rueckfall_gelesen()
    {
        Datei(Path.Combine(Wurzel, "var", "log", "syslog"), "Nov 9 kernel: EXT4-fs error (device sda1): bad block\nNov 9 cron: alles gut\n");
        var r = Lauf();
        Assert.Equal(Status.Warnung, r.Status);
        Assert.Contains("Klassisches Syslog", r.Ausgabe);
    }

    [Fact]
    public void Ohne_Linux_Installation_gibt_es_nur_eine_Info()
    {
        Directory.Delete(Path.Combine(Wurzel, "var"), true);
        var r = Lauf();
        Assert.Equal(Status.Info, r.Status);
    }
}
