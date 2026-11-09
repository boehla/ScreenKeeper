using System.Windows.Markup;

namespace ScreenKeeper.Resources;

/// <summary>
/// XAML-Zugriff auf <see cref="Strings"/>: <c>{loc:Str Schlüssel}</c> mit
/// <c>xmlns:loc="clr-namespace:ScreenKeeper.Resources"</c>.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class StrExtension : MarkupExtension {
    public StrExtension() {
    }

    public StrExtension(string key) {
        Key = key;
    }

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) {
        return Strings.Get(Key);
    }
}
