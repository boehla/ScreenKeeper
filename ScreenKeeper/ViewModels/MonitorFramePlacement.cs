using System.Windows;

namespace ScreenKeeper.ViewModels;

/// <summary>
/// UI-freie Berechnung der Monitor-Rahmen im Vollbild-Editor (ab 0.16): je Monitor ein Rahmen innen an der Kante, in
/// Ansichts-DIPs, auf Gerätepixel ausgerichtet. Der Rahmen besteht aus vier Balken (oben, unten, links, rechts), die
/// sich nicht überlappen.
/// </summary>
public static class MonitorFramePlacement {
    /// <summary>Stärke des Rahmens in Gerätepixeln.</summary>
    public const int ThicknessDevice = 3;

    /// <summary>Balken der Rahmen aller Monitore (je Monitor vier, bei sehr kleinen Monitoren ein ausgefülltes Rechteck).</summary>
    /// <param name="monitorViews">Monitore in Ansichts-DIPs</param>
    /// <param name="deviceScale">Gerätepixel je DIP</param>
    public static List<Rect> GetBars(IEnumerable<Rect> monitorViews, double deviceScale) {
        List<Rect> bars = new List<Rect>();
        double scale = deviceScale > 0 ? deviceScale : 1;
        double thickness = ThicknessDevice / scale;
        foreach(Rect monitor in monitorViews) {
            if(monitor.IsEmpty) continue;
            // Außenkante auf Gerätepixel runden, damit der Rahmen genau an der Monitorkante liegt und scharf bleibt
            double left = Math.Round(monitor.Left * scale) / scale;
            double top = Math.Round(monitor.Top * scale) / scale;
            double right = Math.Round(monitor.Right * scale) / scale;
            double bottom = Math.Round(monitor.Bottom * scale) / scale;
            double width = right - left;
            double height = bottom - top;
            if(width <= 0 || height <= 0) continue;
            if(width <= 2 * thickness || height <= 2 * thickness) {
                bars.Add(new Rect(left, top, width, height));
                continue;
            }
            bars.Add(new Rect(left, top, width, thickness));
            bars.Add(new Rect(left, bottom - thickness, width, thickness));
            bars.Add(new Rect(left, top + thickness, thickness, height - 2 * thickness));
            bars.Add(new Rect(right - thickness, top + thickness, thickness, height - 2 * thickness));
        }
        return bars;
    }
}
