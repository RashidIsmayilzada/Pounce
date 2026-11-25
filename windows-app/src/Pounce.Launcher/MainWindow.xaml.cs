using System.Windows;
using Pounce.Core;

namespace Pounce.Launcher;

/// <summary>Placeholder window that proves the build pipeline. To be replaced by the real launcher panel.</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = PounceInfo.Name;
        Placeholder.Text = $"{PounceInfo.Name} for Windows";
    }
}
