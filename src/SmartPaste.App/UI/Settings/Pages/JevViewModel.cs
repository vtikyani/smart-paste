using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;

namespace SmartPaste.App.UI.Settings.Pages;

// A screen reader names a list's items by ToString, so each choice reads as its label.
internal sealed record RouteChoice(JevRoute Value, string Label)
{
    public override string ToString() => Label;
}

// The Jev & AI page: keys, where Jev requests go, asking ahead, and the parsing model.
internal sealed partial class JevViewModel : ObservableObject, IDisposable
{
    private readonly SettingsContext _context;
    private bool _loading;

    public JevViewModel(SettingsContext context)
    {
        _context = context;
        OpenRouterKey = new ApiKeyViewModel(ApiKeyService.OpenRouter, context);
        TypeSafeKey = new ApiKeyViewModel(ApiKeyService.TypeSafe, context);
        Load();
        OpenRouterKey.PropertyChanged += OnKeyChanged;
        TypeSafeKey.PropertyChanged += OnKeyChanged;
        context.Services.Settings.Changed += OnSettingsChanged;
        context.Services.State.PropertyChanged += OnStateChanged;
    }

    public ApiKeyViewModel OpenRouterKey { get; }

    public ApiKeyViewModel TypeSafeKey { get; }

    public IReadOnlyList<RouteChoice> Routes { get; } =
    [
        new(JevRoute.Auto, "Automatic"),
        new(JevRoute.TypeSafe, "TypeSafe"),
        new(JevRoute.OpenRouter, "OpenRouter")
    ];

    // Problems with OpenRouter or TypeSafe right now, newest state only.
    public ObservableCollection<ServiceAlert> Alerts { get; } = [];

    [ObservableProperty]
    public partial RouteChoice? SelectedRoute { get; set; }

    [ObservableProperty]
    public partial string RouteSummary { get; set; } = "";

    [ObservableProperty]
    public partial bool AskJevAhead { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDefaultParsingModel))]
    [NotifyCanExecuteChangedFor(nameof(ResetParsingModelCommand))]
    public partial string ParsingModel { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetParsingModelCommand))]
    public partial string ParsingModelError { get; set; } = "";

    public string DefaultParsingModel => AppSettings.DefaultParsingModel;

    public bool IsDefaultParsingModel => ParsingModel == AppSettings.DefaultParsingModel;

    // Which service Jev requests go through for a route, given the saved keys.
    public static string JevService(JevRoute route, bool typeSafeSaved) => route switch
    {
        JevRoute.TypeSafe => "TypeSafe",
        JevRoute.OpenRouter => "OpenRouter",
        _ => typeSafeSaved ? "TypeSafe" : "OpenRouter"
    };

    public static string DescribeRoute(JevRoute route, bool typeSafeSaved, bool openRouterSaved) => route switch
    {
        JevRoute.Auto when typeSafeSaved => "Right now that's TypeSafe.",
        JevRoute.Auto when openRouterSaved => "Right now that's OpenRouter.",
        JevRoute.Auto => "No key is saved yet, so built-in labels pick values for now.",
        JevRoute.TypeSafe when typeSafeSaved => "Jev requests go straight to TypeSafe.",
        JevRoute.TypeSafe => "No TypeSafe key is saved, so built-in labels pick values until you add one.",
        JevRoute.OpenRouter when openRouterSaved => "Jev requests go through OpenRouter.",
        _ => "No OpenRouter key is saved, so built-in labels pick values until you add one."
    };

    // OpenRouter model ids look like "vendor/model", optionally with a ":variant" suffix.
    public static bool IsValidModelId(string id) => ModelId().IsMatch(id);

    partial void OnSelectedRouteChanged(RouteChoice? value)
    {
        if (_loading || value is null) return;
        _context.TrySave(s => s with { JevRoute = value.Value });
        RefreshRoute();
    }

    partial void OnAskJevAheadChanged(bool value)
    {
        if (!_loading) _context.TrySave(s => s with { AskJevAhead = value });
    }

    partial void OnParsingModelChanged(string value)
    {
        if (_loading) return;
        var model = value.Trim();
        if (model.Length == 0)
        {
            ResetParsingModel();
            return;
        }
        if (!IsValidModelId(model))
        {
            ParsingModelError = $"Model ids look like vendor/model, for example {AppSettings.DefaultParsingModel}.";
            return;
        }
        ParsingModelError = "";
        _context.TrySave(s => s with { ParsingModel = model });
    }

    private bool CanResetParsingModel() => !IsDefaultParsingModel || ParsingModelError.Length > 0;

    [RelayCommand(CanExecute = nameof(CanResetParsingModel))]
    private void ResetParsingModel()
    {
        ParsingModelError = "";
        if (_context.TrySave(s => s with { ParsingModel = AppSettings.DefaultParsingModel })) Load();
    }

    public void Dispose()
    {
        OpenRouterKey.PropertyChanged -= OnKeyChanged;
        TypeSafeKey.PropertyChanged -= OnKeyChanged;
        _context.Services.Settings.Changed -= OnSettingsChanged;
        _context.Services.State.PropertyChanged -= OnStateChanged;
        OpenRouterKey.Dispose();
        TypeSafeKey.Dispose();
    }

    private void Load()
    {
        _loading = true;
        try
        {
            var settings = _context.Settings;
            SelectedRoute = Routes.First(r => r.Value == settings.JevRoute);
            AskJevAhead = settings.AskJevAhead;
            // Keep what the user is typing when it isn't a valid id yet.
            if (ParsingModelError.Length == 0) ParsingModel = settings.ParsingModel;
        }
        finally
        {
            _loading = false;
        }
        RefreshRoute();
    }

    private void RefreshRoute()
    {
        var route = SelectedRoute?.Value ?? JevRoute.Auto;
        RouteSummary = DescribeRoute(route, TypeSafeKey.IsSaved, OpenRouterKey.IsSaved);
        var state = _context.Services.State;
        var alerts = ServiceAlerts.For(state.ParsingStatus, state.JevStatus, JevService(route, TypeSafeKey.IsSaved));
        if (alerts.SequenceEqual(Alerts)) return;
        Alerts.Clear();
        foreach (var alert in alerts) Alerts.Add(alert);
    }

    private void OnKeyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ApiKeyViewModel.IsSaved)) RefreshRoute();
    }

    private void OnSettingsChanged(AppSettings previous, AppSettings current) => _context.Post(Load);

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppState.ParsingStatus) or nameof(AppState.JevStatus)) RefreshRoute();
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._\-]*/[A-Za-z0-9][A-Za-z0-9._\-]*(:[A-Za-z0-9._\-]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex ModelId();
}
