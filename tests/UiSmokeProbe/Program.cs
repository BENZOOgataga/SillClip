using System.Reflection;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using WindowSill.API;
using WindowSill.ClipboardHistory.Core;
using WindowSill.ClipboardHistory.Services;
using WindowSill.ClipboardHistory.Settings;
using WindowSill.ClipboardHistory.ViewModels;
using WindowSill.ClipboardHistory.Views;
using Path = System.IO.Path;

namespace UiSmokeProbe;

internal static class Program
{
    internal static int Result = 1;
    [STAThread]
    private static int Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(initialization =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new ProbeApplication();
        });
        return Result;
    }
}

/// <summary>Loads compiled production views on a real XAML thread. Uses isolated metadata and no clipboard payloads or input injection.</summary>
public sealed partial class ProbeApplication : Application
{
    private Window? _window;
    private LocalizerProxy? _localizer;

    internal ProbeApplication()
    {
        InitializeComponent();
        // The SDK reserves these bootstrap hooks for its host; reflection is confined to this isolated test process.
        typeof(LoggingExtensions).GetProperty("LoggerFactory", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(null, NullLoggerFactory.Instance);
        UnhandledException += (_, e) =>
        {
            Console.Error.WriteLine(e.Exception);
            e.Handled = true;
            Program.Result = 1;
            Exit();
        };
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        string root = Path.Combine(Path.GetTempPath(), "SillClipUiProbe", Guid.NewGuid().ToString("N"));
        LibraryStateService? state = null;
        try
        {
            state = new(root);
            await state.LoadAsync();
            var history = new PersistentClipboardService(root, NullLogger.Instance);
            var pins = new PinnedClipboardService(root);
            var workspace = new ClipboardWorkspaceService(root, state, new PasteQueueService(), history, pins, new EmptyHistory());
            var settings = DispatchProxy.Create<ISettingsProvider, SettingsProxy>();
            var localizer = DispatchProxy.Create<ILocalizer, LocalizerProxy>();
            _localizer = (LocalizerProxy)(object)localizer;
            typeof(LocalizerExtensions).GetMethod("SetLocalizer", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
                .Invoke(null, [localizer]);
            _window = new Window { Title = "SillClip UI smoke probe" };
            _window.AppWindow.Move(new Windows.Graphics.PointInt32(-30000, -30000));
            // Show inactive, outside the desktop. Loading and layout run without taking user focus.
            _window.AppWindow.Show(false);
            int cases = 0;
            foreach (string language in new[] { "en-US", "fr-FR" })
            {
                ((LocalizerProxy)(object)localizer).Language = language;
                foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
                {
                    using var popupModel = new ClipboardHistoryPopupViewModel(history, workspace);
                    var controls = new CaptureControlsViewModel(state);
                    var popup = new ClipboardHistoryPopupContent(popupModel);
                    await LayoutAsync(popup, theme, 360, 400);
                    cases++;
                    foreach (int width in new[] { 520, 760 })
                    {
                        using var view = new ClipboardWorkspaceView(popupModel, controls, () => new NoOpLease(), (_, _, _) => Task.FromResult(false));
                        for (int tab = 0; tab < 6; tab++)
                        {
                            view.ViewModel.SelectedTabIndex = tab;
                            await LayoutAsync(view, theme, width, 640);
                            cases++;
                        }
                    }
                    foreach (int width in new[] { 520, 1000 })
                    {
                        var view = new SettingsView(settings, new GitHubUpdateService(), state);
                        var scroller = new ScrollViewer { Content = view, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
                        await LayoutAsync(scroller, theme, width, 800);
                        cases++;
                    }
                }
            }
            Program.Result = 0;
            Console.WriteLine($"{cases} compiled WinUI view/layout cases passed: popup, all library tabs, settings, two widths, English/French, light/dark. No user clipboard payload was accessed.");
            if (Environment.GetCommandLineArgs().Contains("--render"))
            {
                using var model = new ClipboardHistoryPopupViewModel(history, workspace);
                var controls = new CaptureControlsViewModel(state);
                await RenderSceneAsync(new ClipboardHistoryPopupContent(model), "popup", 360, 400);
                using var library = new ClipboardWorkspaceView(model, controls, () => new NoOpLease(), (_, _, _) => Task.FromResult(false));
                await RenderSceneAsync(library, "library", 760, 640);
                var settingsView = new ScrollViewer { Content = new SettingsView(settings, new GitHubUpdateService(), state) };
                await RenderSceneAsync(settingsView, "settings", 1000, 1000);
            }
            string? preview = Environment.GetCommandLineArgs().FirstOrDefault(value => value.StartsWith("--preview=", StringComparison.Ordinal));
            if (preview is not null)
            {
                using var model = new ClipboardHistoryPopupViewModel(history, workspace);
                var controls = new CaptureControlsViewModel(state);
                FrameworkElement content = preview switch
                {
                    "--preview=settings" => new ScrollViewer { Content = new SettingsView(settings, new GitHubUpdateService(), state) },
                    "--preview=popup" => new ClipboardHistoryPopupContent(model),
                    _ => new ClipboardWorkspaceView(model, controls, () => new NoOpLease(), (_, _, _) => Task.FromResult(false))
                };
                await LayoutAsync(content, ElementTheme.Dark, preview == "--preview=popup" ? 360 : 760, preview == "--preview=popup" ? 400 : 640);
                _window.Title = "SillClip UI preview (isolated test data)";
                _window.AppWindow.Move(new Windows.Graphics.PointInt32(100, 80));
                _window.AppWindow.Show(false);
                // The preview closes itself; it never writes to the Windows clipboard or sends keyboard input.
                await Task.Delay(TimeSpan.FromMinutes(5));
                (content as IDisposable)?.Dispose();
            }
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); Program.Result = 1; }
        finally
        {
            if (_window is not null) { _window.Content = null; _window.Close(); }
            state?.Dispose();
            string parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "SillClipUiProbe")) + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(root).StartsWith(parent, StringComparison.OrdinalIgnoreCase) && Directory.Exists(root)) Directory.Delete(root, recursive: true);
            Exit();
        }
    }

    private async Task LayoutAsync(FrameworkElement content, ElementTheme theme, int width, int height)
    {
        content.RequestedTheme = theme;
        if (!ReferenceEquals(_window!.Content, content)) _window.Content = content;
        double scale = content.XamlRoot?.RasterizationScale ?? 1;
        _window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)Math.Round(width * scale), (int)Math.Round(height * scale)));
        for (int attempt = 0; attempt < 120; attempt++)
        {
            await Task.Delay(16);
            content.UpdateLayout();
            bool selectedContentReady = content is not ClipboardWorkspaceView workspaceView
                || workspaceView.ViewModel.SelectedTabIndex != 0
                || workspaceView.FindName("BrowseList") is ListView { IsLoaded: true };
            if (content.IsLoaded && content.ActualWidth > 0 && content.ActualHeight > 0 && selectedContentReady) break;
        }
        if (_localizer is not null) ApplyUidStrings(content, _localizer);
        content.UpdateLayout();
        if (!double.IsFinite(content.ActualWidth) || content.ActualWidth <= 0) throw new InvalidOperationException("The view did not complete layout.");
        if (content is ClipboardWorkspaceView library && library.ViewModel.SelectedTabIndex == 0
            && library.FindName("BrowseList") is ListView list && (!list.IsLoaded || list.ActualHeight < 160))
            throw new InvalidOperationException("The browse list did not use the available page height. " + Ancestors(list));
    }

    private static void ApplyUidStrings(DependencyObject element, LocalizerProxy localizer)
    {
        string uid = Uids.GetUid(element);
        if (!string.IsNullOrEmpty(uid))
        {
            foreach (var (name, value) in localizer.PropertiesFor(uid))
            {
                string propertyName = name.EndsWith("Property", StringComparison.Ordinal) ? name[..^8] : name;
                var property = element.GetType().GetProperty(propertyName);
                if (property?.CanWrite == true && (property.PropertyType == typeof(string) || property.PropertyType == typeof(object)))
                    property.SetValue(element, value);
            }
        }
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            ApplyUidStrings(VisualTreeHelper.GetChild(element, index), localizer);
    }

    private async Task RenderSceneAsync(FrameworkElement content, string name, int width, int height)
    {
        var frame = (Grid)Microsoft.UI.Xaml.Markup.XamlReader.Load("<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" RequestedTheme=\"Dark\" Background=\"{ThemeResource ApplicationPageBackgroundThemeBrush}\" />");
        frame.Children.Add(content);
        await LayoutAsync(frame, ElementTheme.Dark, width, height);
        await Task.Delay(400);
        frame.UpdateLayout();
        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
        await bitmap.RenderAsync(frame);
        var pixels = await bitmap.GetPixelsAsync();
        string folder = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "artifacts", "major-release", "ui"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name + ".png");
        await using var output = File.Create(path);
        using var stream = output.AsRandomAccessStream();
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(pixels));
        await encoder.FlushAsync();
        Console.WriteLine("Rendered isolated UI scene: " + name);
    }

    private static string Ancestors(DependencyObject element)
    {
        var sizes = new List<string>();
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is FrameworkElement view)
                sizes.Add($"{view.GetType().Name}({view.Name})={view.ActualWidth:0}x{view.ActualHeight:0}/{view.VerticalAlignment}");
        return string.Join(" > ", sizes);
    }

    private sealed class NoOpLease : IDisposable { public void Dispose() { } }
    private sealed class EmptyHistory : ILiveClipboardHistory
    {
        public IReadOnlyList<ClipboardItemData> GetCachedItems() => [];
        public Task DeleteBySignatureAsync(string signature) => throw new InvalidOperationException("UI smoke probes cannot delete clipboard data.");
        public Task ClearAsync() => throw new InvalidOperationException("UI smoke probes cannot clear clipboard data.");
    }
}

public class SettingsProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == "GetSetting") return args![0]!.GetType().GetProperty("DefaultValue")!.GetValue(args[0]);
        if (targetMethod?.ReturnType == typeof(Task)) return Task.CompletedTask;
        return targetMethod?.ReturnType == typeof(bool) ? false : null;
    }
}

public class LocalizerProxy : DispatchProxy
{
    internal string Language = "en-US";
    private readonly Dictionary<string, Dictionary<string, string>> _languages = Load();
    internal IEnumerable<KeyValuePair<string, string>> PropertiesFor(string uid)
        => _languages[Language].Where(pair => pair.Key.StartsWith(uid + ".", StringComparison.Ordinal))
            .Select(pair => new KeyValuePair<string, string>(pair.Key[(uid.Length + 1)..], pair.Value));

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        return targetMethod?.Name switch
        {
            "GetAvailableLanguages" => new[] { "en-US", "fr-FR" },
            "GetCurrentLanguage" => Language,
            "SetLanguageAsync" => Task.CompletedTask,
            "GetLocalizedString" => _languages[Language].GetValueOrDefault((string)args![0]!, _languages["en-US"].GetValueOrDefault((string)args[0]!, string.Empty)),
            _ => null
        };
    }

    private static Dictionary<string, Dictionary<string, string>> Load()
    {
        var result = new Dictionary<string, Dictionary<string, string>>();
        foreach (string language in new[] { "en-US", "fr-FR" })
        {
            var values = new Dictionary<string, string>();
            foreach (string file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Strings", language), "*.resw"))
                foreach (var entry in XDocument.Load(file).Root!.Elements("data"))
                    values["/WindowSill.ClipboardHistory/" + Path.GetFileNameWithoutExtension(file) + "/" + entry.Attribute("name")!.Value] = entry.Element("value")!.Value;
            result[language] = values;
        }
        return result;
    }
}
