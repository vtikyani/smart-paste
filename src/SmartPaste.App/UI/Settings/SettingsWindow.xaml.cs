using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SmartPaste.App.Services;
using SmartPaste.App.UI.Navigation;
using SmartPaste.App.UI.Settings.Pages;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;

namespace SmartPaste.App.UI.Settings;

// The settings window: a Fluent window with the page list on the left. Pages are built on first visit and kept
// while the window is open; everything they subscribed to is released when it closes.
internal partial class SettingsWindow : FluentWindow, INavigationViewPageProvider
{
    private static readonly IReadOnlyDictionary<string, Type> PageTypes = new Dictionary<string, Type>
    {
        [SettingsPages.General] = typeof(GeneralPage),
        [SettingsPages.Jev] = typeof(JevPage),
        [SettingsPages.Privacy] = typeof(PrivacyPage),
        [SettingsPages.LearnedLabels] = typeof(LearnedLabelsPage),
        [SettingsPages.Shortcuts] = typeof(ShortcutsPage),
        [SettingsPages.About] = typeof(AboutPage)
    };

    private readonly SettingsContext _context;
    private readonly SettingsViewModel _viewModel;
    private readonly Dictionary<Type, FrameworkElement> _pages = [];
    private readonly List<IDisposable> _owned = [];
    private string _pendingPage;

    public SettingsWindow(UiServices services, IWindowNavigator navigator, string page)
    {
        InitializeComponent();
        WindowSizing.FitToWorkArea(this);
        _context = new SettingsContext(services, navigator, new ContentDialogPrompts(DialogHost), Dispatcher);
        _viewModel = new SettingsViewModel(_context);
        _viewModel.PageRequested += ShowPage;
        DataContext = _viewModel;
        Navigation.SetPageProviderService(this);
        Navigation.Navigated += OnNavigated;
        _pendingPage = page;
        Loaded += (_, _) => ShowPage(_pendingPage);
        Closed += (_, _) => ReleaseAll();
    }

    // The page on screen (one of SettingsPages), or null before the first page is shown.
    public string? CurrentPage { get; private set; }

    public void ShowPage(string page)
    {
        if (!PageTypes.ContainsKey(page)) page = SettingsPages.General;
        if (!IsLoaded)
        {
            _pendingPage = page;
            return;
        }
        if (Navigation.Navigate(PageTypes[page])) CurrentPage = page;
    }

    public object? GetPage(Type pageType)
    {
        if (_pages.TryGetValue(pageType, out var existing)) return existing;
        var page = CreatePage(pageType);
        _pages[pageType] = page;
        return page;
    }

    private void OnNavigated(NavigationView sender, NavigatedEventArgs args)
    {
        if (args.Page is not FrameworkElement page) return;
        CurrentPage = PageTypes.FirstOrDefault(p => p.Value == page.GetType()).Key ?? CurrentPage;
        // All pages share one scroller; a page someone opens starts at its top, not where the last one was left.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => ScrollerAround(page)?.ScrollToTop());
    }

    private static ScrollViewer? ScrollerAround(DependencyObject element)
    {
        for (var parent = VisualTreeHelper.GetParent(element); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is ScrollViewer scroller) return scroller;
        return null;
    }

    private FrameworkElement CreatePage(Type pageType)
    {
        if (pageType == typeof(GeneralPage)) return new GeneralPage(Own(new GeneralViewModel(_context)));
        if (pageType == typeof(JevPage)) return new JevPage(Own(new JevViewModel(_context)));
        if (pageType == typeof(PrivacyPage)) return new PrivacyPage(Own(new PrivacyViewModel(_context)));
        if (pageType == typeof(LearnedLabelsPage)) return new LearnedLabelsPage(Own(new LearnedLabelsViewModel(_context)));
        if (pageType == typeof(ShortcutsPage)) return new ShortcutsPage(Own(new ShortcutsViewModel(_context)));
        if (pageType == typeof(AboutPage)) return new AboutPage(Own(new AboutViewModel(_context)));
        throw new ArgumentOutOfRangeException(nameof(pageType), pageType, "Not a settings page.");
    }

    private T Own<T>(T item) where T : IDisposable
    {
        _owned.Add(item);
        return item;
    }

    private void ReleaseAll()
    {
        Navigation.Navigated -= OnNavigated;
        _viewModel.PageRequested -= ShowPage;
        _viewModel.Dispose();
        foreach (var item in _owned) item.Dispose();
        _owned.Clear();
    }
}
