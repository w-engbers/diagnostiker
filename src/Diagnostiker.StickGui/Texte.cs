using static Diagnostiker.StickKern.Sprache;

namespace Diagnostiker.StickGui;

/// <summary>
/// Alle Texte der Oberfläche an einer Stelle. Der deutsche Text ist der Schlüssel, Englisch und Niederländisch stehen im <c>Katalog</c> des Kerns.
/// Es sind Eigenschaften (keine Konstanten), damit ein Sprachwechsel sofort wirkt.
/// </summary>
static class Texte
{
    public static string Titel => T("Diagnostiker – Stick-Manager");
    public static string UntertitelPaket => T("Schritt 1 von 4: Paket laden und prüfen");
    public static string UntertitelStick => T("Schritt 2 von 4: Stick wählen");
    public static string UntertitelBestaetigen => T("Schritt 3 von 4: Bestätigen");
    public static string UntertitelSchreiben => T("Schritt 4 von 4: Stick wird erstellt");
    public static string UntertitelErgebnis => T("Ergebnis");

    public static string PaketSuche => T("Neueste Version wird gesucht …");
    public static string PaketLaedt => T("Paket wird geladen und geprüft …");
    public static string PaketOk(string version) => T("Version {0}: Die Signatur des Herausgebers ist in Ordnung.", version);
    public static string PaketFehler => T("Das Paket lässt sich nicht laden oder ist nicht in Ordnung:");
    public static string KeineVerbindung(string meldung) => T("Keine Verbindung oder das Paket ist nicht erreichbar. {0}", meldung);
    public static string PaketOrdnerWaehlen => T("Paket aus Ordner wählen …");
    public static string PaketNochmal => T("Erneut versuchen");
    public static string PaketOrdnerTitel => T("Ordner mit manifest.json wählen");
    public static string Fortschritt(string name, string bytes, string gesamt) => T("{0}: {1} von {2}", name, bytes, gesamt);

    public static string StickErklaerung => T("Gesteckte USB-Sticks erscheinen hier von selbst. Angeboten werden nur USB-Sticks von 4 bis 512 GiB, die das System nicht benutzt.");
    public static string StickKeiner => T("Kein geeigneter USB-Stick gefunden. Bitte einen Stick einstecken.");
    public static string LaufwerkeNichtLesbar(string meldung) => T("Die Laufwerke lassen sich nicht lesen: {0}", meldung);
    public static string Weiter => T("Weiter");
    public static string StickPruefung => T("Dieser Stick wird für den Diagnostiker vollständig überschrieben.");

    public static string BestaetigenWarnung(string pfad) => T("ALLES auf {0} wird gelöscht und lässt sich nicht wiederherstellen.", pfad);
    public static string BestaetigenFrage(string name) => T("Zur Bestätigung den Gerätenamen eingeben ({0}):", name);
    public static string BestaetigenStart => T("Stick jetzt erstellen");
    public static string Zurueck => T("Zurück");

    public static string SchreibenVorbereitung => T("Stick wird vorbereitet …");
    public static string SchreibenPasswort => T("Bitte das Passwort eingeben, wenn ein Fenster danach fragt. Den Stick nicht abziehen.");
    public static string SchreibenLaeuft => T("Der Stick wird beschrieben. Das kann je nach Stick einige Minuten dauern, auch wenn die Anzeige zwischendurch kaum weiterrückt. Bitte den Stick nicht abziehen.");
    public static string Geschwindigkeit(string wert) => T("Geschwindigkeit: {0}", wert);
    public static string SchreibenDetails => T("Einzelheiten");

    public static string ErgebnisErfolg => T("Der Stick ist fertig.");
    public static string ErgebnisFehler => T("Der Stick konnte nicht erstellt werden.");
    public static string ErgebnisNochEiner => T("Weiteren Stick erstellen");
    public static string ErgebnisSchliessen => T("Schließen");
    public static string ErgebnisProtokoll(string pfad) => T("Protokoll: {0}", pfad);
    public static string Unerwartet(string meldung) => T("Unerwarteter Fehler: {0}", meldung);

    public static string SchliessenGesperrt => T("Der Stick wird gerade geschrieben. Bitte warten, bis der Vorgang beendet ist.");
}
