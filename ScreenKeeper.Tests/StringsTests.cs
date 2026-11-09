using System.Reflection;
using System.Resources;
using System.Globalization;
using System.Collections;
using ScreenKeeper.Resources;

namespace ScreenKeeper.Tests;

public class StringsTests {
    [Fact]
    public void EveryProperty_HasResxEntry() {
        PropertyInfo[] properties = typeof(Strings).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(string))
            .ToArray();
        Assert.NotEmpty(properties);
        foreach(PropertyInfo property in properties) {
            string value = (string)property.GetValue(null)!;
            Assert.False(value.StartsWith('[') && value.EndsWith(']'), $"Schlüssel {property.Name} fehlt in Strings.resx");
        }
    }

    [Fact]
    public void EveryResxEntry_HasProperty() {
        ResourceSet? set = Strings.ResourceManager.GetResourceSet(CultureInfo.InvariantCulture, true, true);
        Assert.NotNull(set);
        HashSet<string> properties = typeof(Strings).GetProperties(BindingFlags.Public | BindingFlags.Static).Select(p => p.Name).ToHashSet();
        foreach(DictionaryEntry entry in set!) {
            Assert.Contains((string)entry.Key, properties);
        }
    }

    [Fact]
    public void Texts_AreGerman() {
        Assert.Equal("Beenden", Strings.MenuExit);
        Assert.Equal("History öffnen", Strings.MenuHistory);
    }
}
