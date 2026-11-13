namespace Diagnostiker.StickKern;

/// <summary>Findet den Projektordner (dort liegt <c>stick/schreiben.sh</c>), ausgehend vom Programm- oder Arbeitsordner.</summary>
public static class ProjektOrdner
{
    public static string? Finden(params string[] startOrdner)
    {
        foreach (var start in startOrdner)
        {
            for (var d = new DirectoryInfo(start); d is not null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "stick", "schreiben.sh"))) return d.FullName;
        }
        return null;
    }
}
