using System.Windows.Threading;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;
using SmartPaste.Core;

namespace SmartPaste.App.Tests.Services;

public sealed class ProfileStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sp-profiles-" + Guid.NewGuid().ToString("N"));
    private readonly AppState _state = new();
    private readonly ProfileStore _store;

    public ProfileStoreTests()
    {
        var settings = new SettingsStore(Path.Combine(_dir, "settings.json"));
        // Built on the test thread, so state updates happen inline.
        _store = new ProfileStore(_state, settings, Dispatcher.CurrentDispatcher);
    }

    public void Dispose()
    {
        _store.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static Profile Block(params (string Key, string Value)[] fields) =>
        new([new Entity("person", fields.ToDictionary(f => f.Key, f => f.Value))], DateTimeOffset.UtcNow);

    private static readonly Profile Contact = Block((FieldKeys.FirstName, "Jordan"), (FieldKeys.Email, "jordan@example.com"));

    [Fact]
    public void ArmsTheCurrentCopy()
    {
        var (generation, _) = _store.BeginCopy();
        Assert.True(_store.Arm(generation, Contact, ProfileOrigin.CopiedText, parsedByAi: false));
        Assert.Same(Contact, _store.Current?.Profile);
        Assert.Same(Contact, _state.Armed?.Profile);
    }

    [Fact]
    public void AnOlderCopyCanNeverReArm()
    {
        var (first, firstToken) = _store.BeginCopy();
        var (second, _) = _store.BeginCopy();
        // The slow AI parse of the first copy finishes after the second copy happened.
        Assert.True(firstToken.IsCancellationRequested);
        Assert.False(_store.Arm(first, Contact, ProfileOrigin.CopiedText, parsedByAi: true));
        Assert.Null(_store.Current);
        Assert.True(_store.IsCurrent(second));
    }

    [Fact]
    public void ClearDropsTheBlockAndCancelsItsWork()
    {
        var (generation, token) = _store.BeginCopy();
        _store.Arm(generation, Contact, ProfileOrigin.CopiedText, parsedByAi: false);
        _store.Clear();
        Assert.Null(_store.Current);
        Assert.Null(_state.Armed);
        Assert.True(token.IsCancellationRequested);
        Assert.False(_store.Arm(generation, Contact, ProfileOrigin.CopiedText, parsedByAi: true));
    }

    [Fact]
    public void ARicherParseKeepsTheOriginalCountdown()
    {
        var (generation, _) = _store.BeginCopy();
        _store.Arm(generation, Contact, ProfileOrigin.CopiedText, parsedByAi: false);
        var first = _store.Current!;
        var richer = Block((FieldKeys.FirstName, "Jordan"), (FieldKeys.Email, "jordan@example.com"), (FieldKeys.Phone, "201-555-0100"));
        _store.Arm(generation, richer, ProfileOrigin.CopiedText, parsedByAi: true);
        Assert.Equal(first.ArmedAt, _store.Current!.ArmedAt);
        Assert.Equal(first.ExpiresAt, _store.Current.ExpiresAt);
        Assert.True(_store.Current.ParsedByAi);
    }

    [Fact]
    public void ASingleFieldIsNeverArmed()
    {
        var (generation, _) = _store.BeginCopy();
        Assert.False(_store.Arm(generation, Block((FieldKeys.Phone, "201-555-0100")), ProfileOrigin.CopiedText, parsedByAi: false));
        Assert.Null(_store.Current);
    }
}
