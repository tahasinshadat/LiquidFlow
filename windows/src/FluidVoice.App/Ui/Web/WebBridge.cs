using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using FluidVoice.Ai;
using FluidVoice.Core;
using FluidVoice.Stt;
using Microsoft.Web.WebView2.Core;

namespace FluidVoice.Ui.Web;

/// <summary>
/// JSON-RPC bridge between the React front-end (running in WebView2) and the native C# core.
/// The web side calls <c>window.chrome.webview.postMessage(JSON.stringify({id, method, args}))</c>;
/// we dispatch to a handler and reply with <c>{id, ok, result|error}</c>. Push events (no id)
/// carry live state (dictation status, download progress) to the UI.
/// This is the single seam through which the web UI reads and drives the app.
/// </summary>
public sealed class WebBridge
{
    private readonly CoreWebView2 _core;
    private readonly Dispatcher _dispatcher;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public WebBridge(CoreWebView2 core, Dispatcher dispatcher)
    {
        _core = core;
        _dispatcher = dispatcher;
        _core.WebMessageReceived += OnMessage;
    }

    /// <summary>Push a fire-and-forget event to the web UI (e.g. dictation state changes).</summary>
    public void Emit(string evt, object? payload = null)
    {
        _dispatcher.BeginInvoke(() =>
        {
            try { _core.PostWebMessageAsJson(JsonSerializer.Serialize(new { evt, payload }, Json)); }
            catch { /* view may be gone */ }
        });
    }

    private async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        int id = -1;
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            id = root.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : -1;
            var method = root.GetProperty("method").GetString() ?? "";
            var args = root.TryGetProperty("args", out var a) ? a : default;
            Log.Info("webbridge", $"call {method}");
            var result = await Handle(method, args);
            Reply(id, true, result, null);
        }
        catch (Exception ex)
        {
            Log.Warn("webbridge", $"call failed: {ex.Message}");
            Reply(id, false, null, ex.Message);
        }
    }

    private void Reply(int id, bool ok, object? result, string? error)
    {
        try { _core.PostWebMessageAsJson(JsonSerializer.Serialize(new { id, ok, result, error }, Json)); }
        catch { }
    }

    // ---- method dispatch ----

    private Task<object?> Handle(string method, JsonElement args) => method switch
    {
        "getState" => Task.FromResult<object?>(GetState()),
        "getModels" => Task.FromResult<object?>(GetModels()),
        "selectModel" => Task.FromResult<object?>(SelectModel(Str(args, "id"))),
        "downloadModel" => DownloadModel(Str(args, "id")),
        "getHistory" => Task.FromResult<object?>(GetHistory(Str(args, "query"))),
        "deleteHistory" => Task.FromResult<object?>(DeleteHistory(args)),
        "clearHistory" => Task.FromResult<object?>(ClearHistory()),
        "getStats" => Task.FromResult<object?>(GetStats()),
        "getProviders" => Task.FromResult<object?>(GetProviders()),
        "selectProvider" => Task.FromResult<object?>(SelectProvider(Str(args, "id"))),
        "setApiKey" => Task.FromResult<object?>(SetApiKey(Str(args, "id"), Str(args, "key"))),
        "getPrompt" => Task.FromResult<object?>(GetPrompt()),
        "setPrompt" => Task.FromResult<object?>(SetPrompt(Str(args, "body"))),
        "getSettings" => Task.FromResult<object?>(Settings.Current),
        "setSetting" => Task.FromResult<object?>(SetSetting(Str(args, "key"), args.GetProperty("value"))),
        "getDictionary" => Task.FromResult<object?>(GetDictionary()),
        "getLearned" => Task.FromResult<object?>(GetLearned()),
        _ => throw new InvalidOperationException($"unknown method '{method}'"),
    };

    // ---- aggregate state for first paint ----

    private object GetState() => new
    {
        name = FirstName(),
        greeting = Greeting(),
        theme = Settings.Current.Theme.ToString(),
        font = Settings.Current.AppFont,
        uiScale = Settings.Current.UiScale,
        version = App.Updater.ThisVersion,
        hotkey = Settings.Current.PrimaryDictationShortcuts.FirstOrDefault()?.DisplayString ?? "not set",
        selectedModel = SpeechModels.Selected().DisplayName,
        selectedModelId = Settings.Current.SelectedSpeechModel,
        aiProvider = Settings.Current.SelectedProviderID,
        setupTested = Settings.Current.SetupTested,
        stats = GetStats(),
        models = GetModels(),
        providers = GetProviders(),
    };

    private object GetStats() => new
    {
        totalWords = HistoryStore.TotalWords,
        wpm = Settings.Current.UserTypingWPM,
        streak = HistoryStore.CurrentStreakDays,
        wordsToday = HistoryStore.WordsToday,
        aiRate = HistoryStore.AiEnhancementRate,
        topApps = HistoryStore.TopApps(5).Select(a => new { app = a.App, count = a.Count }),
        daily = HistoryStore.DailyWordCounts(14).Select(d => new { date = d.Date.ToString("yyyy-MM-dd"), words = d.Words }),
    };

    private object[] GetModels() => SpeechModels.All.Select(m => (object)new
    {
        id = m.Id,
        name = m.DisplayName,
        tagline = m.Tagline,
        description = m.Description,
        size = m.SizeDisplay,
        ram = m.RamEstimate,
        languages = m.LanguageSupport,
        speed = m.SpeedPercent,
        accuracy = m.AccuracyPercent,
        badge = m.Badge,
        engine = m.Engine.ToString(),
        livePreview = m.SupportsLivePreview || m.Engine == SpeechEngineKind.Parakeet,
        downloaded = m.IsDownloaded,
        selected = Settings.Current.SelectedSpeechModel == m.Id,
    }).ToArray();

    private object SelectModel(string id)
    {
        if (SpeechModels.ById(id) is null) throw new InvalidOperationException("no such model");
        Settings.Current.SelectedSpeechModel = id;
        Settings.Current.Save("model");
        return new { ok = true, models = GetModels() };
    }

    private async Task<object?> DownloadModel(string id)
    {
        var model = SpeechModels.ById(id) ?? throw new InvalidOperationException("no such model");
        var progress = new Progress<ModelPreparationProgress>(p =>
        {
            if (p.Phase == ModelPreparationPhase.Downloading)
                Emit("modelProgress", new { id, fraction = p.Fraction });
        });
        await ModelDownloader.DownloadModelAsync(model, progress, CancellationToken.None);
        Settings.Current.SelectedSpeechModel = id;
        Settings.Current.Save("model");
        Emit("modelProgress", new { id, fraction = 1.0, done = true });
        return new { ok = true, models = GetModels() };
    }

    private object[] GetHistory(string query) => HistoryStore.Search(query ?? "").Take(200).Select(en => (object)new
    {
        id = en.Id.ToString(),
        timestamp = en.Timestamp.ToString("o"),
        text = en.ProcessedText,
        app = en.AppName,
        words = en.WordCount,
        ai = en.WasAIProcessed,
        cancelled = en.WasCancelled,
    }).ToArray();

    private object DeleteHistory(JsonElement args)
    {
        var ids = new List<Guid>();
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("ids", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var el in arr.EnumerateArray())
                if (Guid.TryParse(el.GetString(), out var g)) ids.Add(g);
        HistoryStore.DeleteEntries(ids);
        return new { ok = true };
    }

    private object ClearHistory() { HistoryStore.ClearAll(); return new { ok = true }; }

    private object[] GetProviders() => ProviderCatalog.All().Select(p => (object)new
    {
        id = p.Id,
        name = p.Name,
        group = ProviderCatalog.Group(p.Id),
        needsKey = ProviderCatalog.NeedsApiKey(p.Id),
        hasKey = !string.IsNullOrEmpty(CredentialStore.GetApiKey(p.Id)),
        configured = ProviderCatalog.IsConfigured(p.Id),
        selected = Settings.Current.SelectedProviderID == p.Id,
        models = ProviderCatalog.CuratedModels(p.Id),
        selectedModel = ProviderCatalog.SelectedModelFor(p.Id),
    }).ToArray();

    private object SelectProvider(string id)
    {
        Settings.Current.SelectedProviderID = id ?? "";
        Settings.Current.Save("ai");
        return new { ok = true, providers = GetProviders() };
    }

    private object SetApiKey(string id, string key)
    {
        CredentialStore.SetApiKey(id, key ?? "");
        return new { ok = true };
    }

    private object GetPrompt() => new
    {
        body = PromptStore.EffectiveBody(PromptMode.Dictate),
        builtIn = PromptStore.BuiltInBody(PromptMode.Dictate),
        customized = PromptStore.HasOverride(PromptMode.Dictate),
    };

    private object SetPrompt(string body)
    {
        PromptStore.SetOverride(PromptMode.Dictate, body);
        return new { ok = true, customized = PromptStore.HasOverride(PromptMode.Dictate) };
    }

    private object GetDictionary() => Settings.Current.CustomDictionaryEntries.Select(en => (object)new
    {
        id = en.Id,
        triggers = en.Triggers,
        replacement = en.Replacement,
    }).ToArray();

    private object GetLearned() => Settings.Current.LearnedCorrections
        .Where(c => !c.Dismissed)
        .Select(c => (object)new { from = c.From, to = c.To, count = c.Count, promoted = c.Promoted }).ToArray();

    /// <summary>Whitelisted generic setter: simple scalar settings by property name, with type coercion.</summary>
    private object SetSetting(string key, JsonElement value)
    {
        var prop = typeof(Settings).GetProperty(key);
        if (prop is null || !prop.CanWrite) throw new InvalidOperationException($"unknown setting '{key}'");
        object? coerced;
        var t = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
        if (t == typeof(bool)) coerced = value.GetBoolean();
        else if (t == typeof(int)) coerced = value.GetInt32();
        else if (t == typeof(double)) coerced = value.GetDouble();
        else if (t == typeof(float)) coerced = (float)value.GetDouble();
        else if (t == typeof(string)) coerced = value.GetString();
        else if (t.IsEnum) coerced = Enum.Parse(t, value.GetString() ?? "", ignoreCase: true);
        else throw new InvalidOperationException($"unsupported setting type for '{key}'");
        prop.SetValue(Settings.Current, coerced);
        Settings.Current.Save(key);
        return new { ok = true };
    }

    // ---- helpers ----

    private static string Str(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";

    private static string Greeting() => DateTime.Now.Hour switch { < 12 => "Good morning", < 17 => "Good afternoon", _ => "Good evening" };

    private static string FirstName()
    {
        var display = Settings.Current.DisplayName;
        if (!string.IsNullOrWhiteSpace(display))
            return char.ToUpperInvariant(display.Trim().Split(' ', '.', '_', '-')[0][0]) + display.Trim().Split(' ', '.', '_', '-')[0][1..];
        var user = Environment.UserName;
        if (string.IsNullOrWhiteSpace(user)) return "there";
        user = user.Split(' ', '.', '_', '-')[0];
        return char.ToUpperInvariant(user[0]) + user[1..];
    }
}
