using System.Diagnostics;
using System.Windows;

namespace ScreenKeeper.Services;

/// <summary>
/// Nur für automatisierte Tests: sammelt WPF-Bindungsfehler (PresentationTraceSources), damit fehlerhafte XAML-Bindungen
/// auch ohne Debugger auffallen.
/// </summary>
public sealed class BindingErrorListener : TraceListener {
    private readonly List<string> messages = new List<string>();

    public IReadOnlyList<string> Messages => messages;

    public static BindingErrorListener Attach() {
        BindingErrorListener listener = new BindingErrorListener();
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        return listener;
    }

    public override void Write(string? message) {
    }

    public override void WriteLine(string? message) {
        if(!string.IsNullOrEmpty(message)) messages.Add(message);
    }
}
