using System.Windows;
using Listy.Core;

namespace Listy.App;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        Text.SetLanguage(LanguagePreference.Load());
        base.OnStartup(e);
    }
}
