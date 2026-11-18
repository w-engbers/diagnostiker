using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Diagnostiker.StickGui;
using Diagnostiker.StickKern;
using Diagnostiker.StickKern.Paket;

[assembly: AvaloniaTestApplication(typeof(Diagnostiker.GuiTests.TestApp))]

namespace Diagnostiker.GuiTests;

public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>Spielt den ganzen Ablauf des Fensters mit Attrappen durch (kein echter Stick, kein Netz) und speichert Bilder der Seiten.</summary>
public sealed class FensterTests : IDisposable
{
    const long GiB = 1L << 30;
    readonly string _wurzel = Directory.CreateTempSubdirectory("guitest").FullName;
    readonly string _bilder = Environment.GetEnvironmentVariable("DIAGNOSTIKER_BILDER") ?? "";
    public void Dispose() => Directory.Delete(_wurzel, true);

    static Laufwerk Stick(string pfad = "/dev/sdc", long groesse = 58 * GiB) => new(
        pfad, pfad[5..], "Flex Line", "Intenso", groesse, "usb", true, false, [new Partition(pfad + "1", null, "ntfs", groesse, [])], []);
    static Laufwerk Fertig(string pfad = "/dev/sdc") => Stick(pfad) with
    {
        Partitionen = [new Partition(pfad + "2", "DIAGBOOT", "vfat", 2 * GiB, []), new Partition(pfad + "3", "DIAGDATEN", "exfat", 50 * GiB, [])],
    };

    sealed class Quelle(Func<IReadOnlyList<Laufwerk>> liste) : ILaufwerkQuelle
    {
        public Task<IReadOnlyList<Laufwerk>> LesenAsync(CancellationToken ct) => Task.FromResult(liste());
    }

    sealed class Befehle(string stickOrdner, int pkexecCode = 0, long abbild = 100_000, Action? beimSchreiben = null) : IBefehlsausfuehrer
    {
        public int Aufrufe;
        public Task<int> AusfuehrenAsync(string programm, IReadOnlyList<string> a, IReadOnlyDictionary<string, string>? u, Action<string> zeile, CancellationToken ct)
        {
            Aufrufe++;
            if (programm is "pkexec" or "/bin/bash")
            {
                beimSchreiben?.Invoke();
                if (pkexecCode != 0) { zeile("FEHLER: Das Laufwerk enthält nicht das geschriebene Abbild (defekter Stick?)."); return Task.FromResult(pkexecCode); }
                zeile("== Abbilddatei prüfen"); zeile("== Laufwerk /dev/sdc (58 GiB) wird vollständig neu angelegt"); zeile("== Abbild schreiben (2562 MiB)");
                zeile($"{abbild / 2} bytes (50 kB) copied, 60 s, 21,9 MB/s"); zeile($"{abbild} bytes (100 kB) copied, 120 s, 22,0 MB/s");
                zeile("== 10:09:33 Schreiben beendet, Zwischenspeicher leeren"); zeile("== 10:09:33 Zurücklesen und vergleichen"); zeile("== 10:09:52 Vergleich in Ordnung");
                zeile("== 10:09:52 Partitionstabelle fertigstellen"); zeile("== 10:09:56 Datenpartition formatieren (exFAT, DIAGDATEN)"); zeile("SCHREIBEN_FERTIG /dev/sdc3");
            }
            if (programm == "udisksctl" && a[0] == "mount") zeile($"Mounted /dev/sdc3 at {stickOrdner}.");
            return Task.FromResult(0);
        }
    }

    (PaketHolen Holen, string Ordner) Paket()
    {
        var (priv, pub) = Signatur.SchluesselErzeugen();
        var abbild = Path.Combine(_wurzel, "boot.img"); File.WriteAllBytes(abbild, Enumerable.Range(0, 100_000).Select(i => (byte)(i * 7)).ToArray());
        var daten = Path.Combine(_wurzel, "daten"); Directory.CreateDirectory(daten); File.WriteAllText(Path.Combine(daten, "stick.json"), "{}");
        var aus = Path.Combine(_wurzel, "paket");
        PaketBauer.Bauen(abbild, daten, aus, "2026.11.18", 2560, priv, null);
        return (new PaketHolen(new Vertrauen([pub]), null, Path.Combine(_wurzel, "cache")), aus);
    }

    static async Task Warten(Func<bool> bedingung, string was, int sekunden = 15)
    {
        var ende = DateTime.UtcNow.AddSeconds(sekunden);
        while (!bedingung())
        {
            if (DateTime.UtcNow > ende) throw new TimeoutException("Zu lange gewartet auf: " + was);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
    }

    void Bild(Window w, string name)
    {
        if (_bilder.Length == 0) return;
        Directory.CreateDirectory(_bilder);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        w.CaptureRenderedFrame()?.Save(Path.Combine(_bilder, name + ".png"));
    }

    static T Finde<T>(Window w, string name) where T : Control => w.FindControl<T>(name) ?? throw new InvalidOperationException(name);

    [AvaloniaFact]
    public async Task Ganzer_Ablauf_von_der_Paketpruefung_bis_zum_fertigen_Stick()
    {
        var (holen, ordner) = Paket();
        var stickOrdner = Path.Combine(_wurzel, "stick"); Directory.CreateDirectory(stickOrdner);
        var steckt = false; var fertig = false;
        var laufwerke = new Quelle(() => fertig ? [Fertig()] : steckt ? [Stick(), Stick("/dev/sdb", 10 * 1024L)] : []);
        var befehle = new Befehle(stickOrdner, beimSchreiben: () => fertig = true);
        var w = new HauptFenster(laufwerke, befehle, ordner, holen, (l, b) => new PaketStickErsteller(l, b) { Pause = TimeSpan.Zero, Nachpruefungen = 2, BinRoot = false })
        { Aktualisierung = TimeSpan.FromMilliseconds(50), ProtokollPfad = Path.Combine(_wurzel, "lauf.log") };
        w.Show();

        // 1 + 2: Paket wird geprüft, dann kommt die Stickauswahl, zunächst ohne Stick
        await Warten(() => Finde<Control>(w, "SeiteStick").IsVisible, "Seite Stick");
        await Warten(() => (Finde<TextBlock>(w, "StickDetail").Text ?? "").Contains("Kein geeigneter"), "Hinweis ohne Stick");
        Assert.Contains("2026.11.18", Finde<TextBlock>(w, "PaketStatus").Text);
        Bild(w, "1-stick-keiner");

        // Der Stick wird gesteckt: er erscheint von selbst; ein 10-KiB-Laufwerk wird nicht angeboten
        steckt = true;
        await Warten(() => Finde<ListBox>(w, "StickListe").ItemCount == 1, "Stick in der Liste");
        Assert.Contains("/dev/sdc", ((IEnumerable<object>)Finde<ListBox>(w, "StickListe").ItemsSource!).First().ToString());
        Assert.False(Finde<Button>(w, "StickWeiter").IsEnabled);
        Finde<ListBox>(w, "StickListe").SelectedIndex = 0;
        Assert.True(Finde<Button>(w, "StickWeiter").IsEnabled);
        Bild(w, "2-stick-gewaehlt");

        // 3: Bestätigung nur mit dem richtigen Gerätenamen
        Click(Finde<Button>(w, "StickWeiter"));
        Assert.True(Finde<Control>(w, "SeiteBestaetigen").IsVisible);
        var eingabe = Finde<TextBox>(w, "BestaetigenEingabe");
        eingabe.Text = "sdb"; Dispatcher.UIThread.RunJobs();
        Assert.False(Finde<Button>(w, "BestaetigenStart").IsEnabled);
        eingabe.Text = "sdc"; Dispatcher.UIThread.RunJobs();
        Assert.True(Finde<Button>(w, "BestaetigenStart").IsEnabled);
        Bild(w, "3-bestaetigen");

        // 4 + 5: Schreiben und Ergebnis
        Click(Finde<Button>(w, "BestaetigenStart"));
        await Warten(() => Finde<Control>(w, "SeiteErgebnis").IsVisible, "Ergebnis");
        Dispatcher.UIThread.RunJobs();          // noch ausstehende Protokollzeilen
        Assert.True(Finde<TextBlock>(w, "ErgebnisTitel").Text == "Der Stick ist fertig.", Finde<TextBlock>(w, "ErgebnisText").Text);
        Assert.Contains("2026.11.18", Finde<TextBlock>(w, "ErgebnisText").Text);
        Assert.Equal(100, Finde<ProgressBar>(w, "SchreibenBalken").Value, 0.5);
        Assert.Contains("== Abbild schreiben", Finde<TextBox>(w, "SchreibenLog").Text);
        Assert.True(File.Exists(Path.Combine(stickOrdner, "stick.json")));
        Bild(w, "5-ergebnis-fertig");
        w.Close();
    }

    [AvaloniaFact]
    public async Task Fehler_des_Helfers_wird_im_Ergebnis_angezeigt()
    {
        var (holen, ordner) = Paket();
        var stickOrdner = Path.Combine(_wurzel, "stick"); Directory.CreateDirectory(stickOrdner);
        var w = new HauptFenster(new Quelle(() => [Stick()]), new Befehle(stickOrdner, pkexecCode: 4), ordner, holen,
            (l, b) => new PaketStickErsteller(l, b) { Pause = TimeSpan.Zero, Nachpruefungen = 1, BinRoot = false }) { Aktualisierung = TimeSpan.FromMilliseconds(50), ProtokollPfad = Path.Combine(_wurzel, "lauf.log") };
        w.Show();
        await Warten(() => Finde<ListBox>(w, "StickListe").ItemCount == 1, "Stick");
        Finde<ListBox>(w, "StickListe").SelectedIndex = 0;
        Click(Finde<Button>(w, "StickWeiter"));
        Finde<TextBox>(w, "BestaetigenEingabe").Text = "sdc";
        Click(Finde<Button>(w, "BestaetigenStart"));
        await Warten(() => Finde<Control>(w, "SeiteErgebnis").IsVisible, "Ergebnis");
        Assert.Equal("Der Stick konnte nicht erstellt werden.", Finde<TextBlock>(w, "ErgebnisTitel").Text);
        Assert.Contains("fehlgeschlagen", Finde<TextBlock>(w, "ErgebnisText").Text);
        Bild(w, "6-ergebnis-fehler");
        Click(Finde<Button>(w, "ErgebnisNochEiner"));          // zurück zur Stickauswahl
        Assert.True(Finde<Control>(w, "SeiteStick").IsVisible);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Unbekanntes_Paket_zeigt_Fehler_und_Wiederholen()
    {
        var (holen, _) = Paket();
        var leer = Path.Combine(_wurzel, "leer"); Directory.CreateDirectory(leer);
        var w = new HauptFenster(new Quelle(() => []), new Befehle(""), leer, holen, null);
        w.Show();
        await Warten(() => Finde<Button>(w, "PaketNochmal").IsVisible, "Fehleranzeige");
        Assert.Contains("nicht", Finde<TextBlock>(w, "PaketStatus").Text);
        Assert.True(Finde<Button>(w, "PaketOrdner").IsVisible);
        Bild(w, "0-paket-fehler");
        w.Close();
    }

    static void Click(Button b) => b.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
}
