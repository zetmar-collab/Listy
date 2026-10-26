using System.Windows.Data;
using System.Windows.Markup;
using Listy.Core;

namespace Listy.App;

[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{key}]") { Source = Text.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
