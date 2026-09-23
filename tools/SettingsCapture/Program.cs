using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using JpScratch.Models;
using JpScratch.Views;

// Render the real settings view without starting App, CLI processes, network calls,
// hotkeys or startup registration. Reflection keeps this developer tool out of the
// application's API. A constructor/header change must fail instead of taking the wrong page.
internal static class Program
{
    private static readonly Assembly AppAssembly = typeof(SettingsWindow).Assembly;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: dotnet run --project tools/SettingsCapture -- <output-directory>");
            return 2;
        }
        string output = Path.GetFullPath(args[0]);
        string data = Path.Combine(Path.GetTempPath(), "jpscratch-settings-render-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(data);
        Environment.SetEnvironmentVariable("JPSCRATCH_DATA_DIR", data);
        try
        {
            RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/JpScratch;component/Themes/Light.xaml", UriKind.Relative) });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/JpScratch;component/Themes/Styles.xaml", UriKind.Relative) });
            var settings = Create("Services.SettingsService");
            var current = (AppSettings)settings.GetType().GetProperty("Current")!.GetValue(settings)!;
            current.Theme = AppTheme.Light;
            current.AutoProofreadingEnabled = false;
            current.StartWithWindows = false;
            var credentials = Create("Services.CredentialService", Path.Combine(data, "credentials.dat"),
                (Func<ApiProvider, string?>)(_ => null));
            var pricing = Create("Services.PricingService", Path.Combine(data, "pricing.json"), null);
            using var database = (IDisposable)Create("Services.Database", ":memory:");
            using var subscriptions = (IDisposable)Create("Proofreading.SubscriptionService", new object?[] { null });
            var window = (SettingsWindow)Create("Views.SettingsWindow", settings, credentials, pricing,
                Create("Services.StyleGuideRepository", database, null), Create("Services.ReactionRepository", database),
                database, (Func<IReadOnlyList<string>>)(() => Array.Empty<string>()),
                (Func<bool>)(() => false), (Func<string, bool>)(_ => false), subscriptions);
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            window.Show();
            try
            {
                var tabs = (TabControl)window.FindName("SettingsTabs");
                (string Header, string File)[] pages =
                [
                    ("全般", "general"), ("エディタ", "editor"), ("校正", "proofreading"),
                    ("学習", "learning"), ("APIキー", "api"), ("料金", "billing"), ("契約サービス", "subscriptions"),
                ];
                if (tabs.Items.Count != pages.Length) throw new InvalidOperationException("Settings navigation changed; update the capture inventory.");
                Directory.CreateDirectory(output);
                foreach (var (header, file) in pages)
                {
                    tabs.SelectedItem = tabs.Items.Cast<TabItem>().Single(item => Equals(item.Header, header));
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    window.UpdateLayout();
                    var content = (FrameworkElement)window.Content;
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth),
                        (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    // Window.Background is outside Window.Content's visual tree.
                    var background = new DrawingVisual();
                    using (var drawing = background.RenderOpen())
                        drawing.DrawRectangle(window.Background, null, new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
                    bitmap.Render(background);
                    bitmap.Render(content);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(output, $"settings-{file}.png"));
                    encoder.Save(stream);
                    Console.WriteLine($"{header}: settings-{file}.png ({bitmap.PixelWidth}x{bitmap.PixelHeight})");
                }
            }
            finally { window.Close(); app.Shutdown(); }
            return 0;
        }
        finally
        {
            // This exact temporary child was created above; never delete a user-supplied path.
            Directory.Delete(data, recursive: true);
        }
    }

    private static object Create(string name, params object?[] args)
        => Activator.CreateInstance(AppAssembly.GetType("JpScratch." + name, throwOnError: true)!,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, args, culture: null)!;
}
