using System.IO;
using FluidVoice.App;
using FluidVoice.Core;
using FluidVoice.Input;
using FluidVoice.Modes;
using FluidVoice.Stt;
using FluidVoice.Text;
using FluidVoice.Typing;
using FluidVoice.Ui;

namespace FluidVoice;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        AppPaths.EnsureCreated();
        Log.Init();
        Settings.Load();

        if (args.Contains("--version"))
        {
            Console.WriteLine("LiquidFlow for Windows 1.6.2 (port of altic-dev/FluidVoice)");
            return 0;
        }
        if (args.Contains("--selftest-stt"))
            return SelfTestStt(args).GetAwaiter().GetResult();
        if (args.Contains("--selftest-llm"))
            return SelfTestLlm(args).GetAwaiter().GetResult();
        if (args.Contains("--selftest-type"))
            return SelfTestType(args);

        using var singleInstance = new SingleInstance();
        if (!singleInstance.IsFirstInstance)
        {
            // Already running: bring the existing window forward instead of a dialog.
            SingleInstance.SignalExistingInstance();
            return 0;
        }

        // rev 2 migration: Parakeet became the default engine (18x faster, higher accuracy).
        // Move whisper users over once it's on disk; they can switch back for non-English.
        if (Settings.Current.SettingsRevision < 2)
        {
            var parakeet = SpeechModels.ById(SpeechModels.ParakeetModelId);
            if (parakeet is { IsDownloaded: true } &&
                Settings.Current.SelectedSpeechModel.StartsWith("whisper", StringComparison.OrdinalIgnoreCase))
            {
                Log.Info("app", $"Migrating speech model {Settings.Current.SelectedSpeechModel} -> {parakeet.Id}");
                Settings.Current.SelectedSpeechModel = parakeet.Id;
            }
            Settings.Current.SettingsRevision = 2;
            Settings.Current.Save("migration");
        }

        var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        CrashGuard.Install(app);
        Styles.Apply(app);

        var overlay = new OverlayWindow();
        var coordinator = new DictationCoordinator(app.Dispatcher, overlay);
        var tray = new TrayIcon();
        var hook = new KeyboardHook();
        var hotkeys = new HotkeyManager(hook, coordinator);

        // Command + Rewrite (Edit) modes
        var commandService = new CommandModeService();
        var mainWindow = new MainWindow(commandService, coordinator);
        var commandWindow = new CommandWindow(commandService);
        var rewriteService = new RewriteModeService();
        var rewriteWindow = new RewriteWindow(rewriteService);
        WindowFx.Apply(commandWindow);
        WindowFx.Apply(rewriteWindow);
        mainWindow.OpenCommandWindow = () => commandWindow.OpenWindow();
        mainWindow.OpenRewriteWindow = () =>
        {
            rewriteService.BeginSession(FocusTracker.Capture());
            rewriteWindow.OpenForSession();
        };

        // When a Command-mode dictation finishes, drop the transcript into the chat and run it.
        coordinator.CommandModeHandler = async text =>
            await app.Dispatcher.InvokeAsync(async () =>
            {
                commandWindow.OpenWindow();
                await commandService.ProcessUserCommandAsync(text);
            }).Task.Unwrap();

        // When a Rewrite-mode dictation finishes, its transcript is the instruction.
        coordinator.RewriteModeHandler = async (text, focus) =>
            await app.Dispatcher.InvokeAsync(async () =>
            {
                rewriteService.BeginSession(focus);
                rewriteWindow.OpenForSession();
                await rewriteService.ApplyInstructionAsync(text, CancellationToken.None);
            }).Task.Unwrap();

        overlay.CancelRequested += () => coordinator.RequestCancel();
        Notifications.ShowHandler = (title, body) => tray.ShowBalloon(title, body);
        coordinator.RecordingStateChanged += recording =>
            app.Dispatcher.BeginInvoke(() => tray.UpdateStatus(recording));

        tray.OpenRequested += () => app.Dispatcher.BeginInvoke(() => ShowMain(mainWindow, "Home"));
        tray.SettingsRequested += () => app.Dispatcher.BeginInvoke(() => ShowMain(mainWindow, "General"));
        tray.DictionaryRequested += () => app.Dispatcher.BeginInvoke(() => ShowMain(mainWindow, "Dictionary"));
        tray.CheckUpdatesRequested += () => _ = CheckForUpdatesAsync(interactive: true);
        tray.QuitRequested += () => app.Dispatcher.BeginInvoke(() =>
        {
            tray.Dispose();
            hook.Dispose();
            Ai.LocalAiServer.Stop();
            app.Shutdown();
        });

        app.Exit += (_, _) =>
        {
            try { tray.Dispose(); } catch { }
            try { hook.Dispose(); } catch { }
            Ai.LocalAiServer.Stop();
        };

        hook.Start();
        coordinator.WarmUpModelInBackground();

        // A dictation app is meant to be always-on, so autostart defaults ON. rev 3 turns it
        // on once for existing installs (a user who explicitly turned it off keeps it off,
        // because after rev 3 we no longer force it).
        if (Settings.Current.SettingsRevision < 3)
        {
            Settings.Current.LaunchAtStartup = true;
            Settings.Current.SettingsRevision = 3;
            Settings.Current.Save("migration");
        }
        StartupManager.Apply(Settings.Current.LaunchAtStartup);

        // Second launches (or the tray/dock click) bring the running window forward.
        singleInstance.StartListening(() => app.Dispatcher.BeginInvoke(() => ShowMain(mainWindow, null)));

        // Show the main window on launch (like the mac app); closing hides to tray.
        mainWindow.Show();
        if (!Settings.Current.OnboardingCompleted)
        {
            Settings.Current.OnboardingCompleted = true;
            Settings.Current.Save();
        }

        if (Settings.Current.AutoUpdateCheckEnabled)
            _ = CheckForUpdatesAsync(interactive: false);

        // Dev seam: show the overlay with sample content for visual checks (no recording, no typing).
        if (Environment.GetEnvironmentVariable("FLUIDVOICE_OVERLAY_PREVIEW") == "1")
        {
            overlay.ShowRecording(Input.RecordingMode.Dictation);
            overlay.SetTargetApp((uint)Environment.ProcessId);
            overlay.SetPreviewText("Hi Joe, comma, new line. Can we meet at 8 a.m. tomorrow?");
            var rng = new Random();
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(66) };
            t.Tick += (_, _) => overlay.SetLevel(0.35f + (float)rng.NextDouble() * 0.5f);
            t.Start();
        }

        Log.Info("app", $"FluidVoice started (hotkey: {Settings.Current.PrimaryDictationShortcuts.FirstOrDefault()?.DisplayString})");
        app.Run();
        return 0;
    }

    private static async Task CheckForUpdatesAsync(bool interactive)
    {
        try
        {
            var update = await App.Updater.CheckAsync(CancellationToken.None);
            if (update is null)
            {
                if (interactive) Notifications.Show("LiquidFlow", "You're on the latest version.");
                return;
            }
            Notifications.Show("Update available",
                $"LiquidFlow {update.Version} is available. Opening download…");
            await App.Updater.DownloadAndRunAsync(update, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Log.Warn("app", $"Update check failed: {ex.Message}");
        }
    }

    private static void ShowMain(MainWindow window, string? tab = null)
    {
        window.Show();
        if (window.WindowState == System.Windows.WindowState.Minimized)
            window.WindowState = System.Windows.WindowState.Normal;
        if (tab is not null) window.SelectTab(tab);
        window.Activate();

        // Reliably pull the window to the foreground even when the request comes from a
        // background thread / another process (Windows blocks plain Activate() then).
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero)
            {
                ShowWindow(hwnd, 9 /*SW_RESTORE*/);
                var fg = GetForegroundWindow();
                uint foreThread = GetWindowThreadProcessId(fg, out _);
                uint ourThread = GetCurrentThreadId();
                if (foreThread != ourThread) AttachThreadInput(ourThread, foreThread, true);
                SetForegroundWindow(hwnd);
                if (foreThread != ourThread) AttachThreadInput(ourThread, foreThread, false);
            }
        }
        catch { }
        window.Topmost = true;
        window.Topmost = false;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    /// <summary>
    /// Headless pipeline check: --selftest-stt &lt;wav&gt; [modelId]
    /// Loads a WAV, resamples to 16k mono, transcribes, runs the formatting pipeline.
    /// </summary>
    private static async Task<int> SelfTestStt(string[] args)
    {
        var idx = Array.IndexOf(args, "--selftest-stt");
        var wavPath = args.Length > idx + 1 ? args[idx + 1] : null;
        var modelId = args.Length > idx + 2 && !args[idx + 2].StartsWith("--") ? args[idx + 2] : SpeechModels.DefaultModelId;
        if (wavPath is null || !File.Exists(wavPath))
        {
            Console.WriteLine("usage: FluidVoice --selftest-stt <wav-file> [modelId]");
            return 2;
        }

        var model = SpeechModels.ById(modelId) ?? SpeechModels.ById(SpeechModels.DefaultModelId)!;
        Console.WriteLine($"[selftest] model: {model.Id} · engine: {model.Engine} ({(model.IsDownloaded ? "cached" : "will download " + model.SizeDisplay)})");

        using ISpeechEngine engine = model.Engine == SpeechEngineKind.Parakeet
            ? new Stt.ParakeetEngine()
            : new Stt.WhisperEngine();
        var progress = new Progress<ModelPreparationProgress>(p =>
        {
            if (p.Phase == ModelPreparationPhase.Downloading)
                Console.Write($"\r[selftest] downloading {(int)(p.Fraction * 100)}%   ");
        });
        await engine.PrepareAsync(model, progress, CancellationToken.None);
        Console.WriteLine("\n[selftest] model loaded");

        var pcm = LoadWavAs16kMono(wavPath);
        Console.WriteLine($"[selftest] audio: {pcm.Length} samples ({pcm.Length / 16000.0:0.0}s)");
        if (pcm.Length < 16000)
        {
            var padded = new float[16000];
            Array.Copy(pcm, padded, pcm.Length);
            pcm = padded;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var raw = await engine.TranscribeAsync(Audio.Dsp.Normalize(pcm), CancellationToken.None);
        sw.Stop();
        Console.WriteLine($"[selftest] transcribed in {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"[selftest] RAW: {raw}");
        var formatted = TranscriptFormatter.Process(raw);
        Console.WriteLine($"[selftest] FORMATTED: {formatted}");

        // exercise the true-streaming path the overlay uses while recording (parakeet only)
        using (var session = engine.TryBeginStreamingSession())
        {
            if (session is not null)
            {
                Console.WriteLine("[selftest] streaming partials (200ms chunks):");
                sw.Restart();
                var last = "";
                const int chunk = 3200; // 200ms @ 16k
                for (int offset = 0; offset < pcm.Length; offset += chunk)
                {
                    var take = Math.Min(chunk, pcm.Length - offset);
                    var slice = new float[take];
                    Array.Copy(pcm, offset, slice, 0, take);
                    var partial = session.Feed(slice);
                    if (partial != last && partial.Length > 0)
                    {
                        Console.WriteLine($"[selftest]   {offset / 16000.0,5:0.0}s → {partial}");
                        last = partial;
                    }
                }
                sw.Stop();
                Console.WriteLine($"[selftest] streaming pass: {sw.ElapsedMilliseconds}ms for {pcm.Length / 16000.0:0.0}s of audio (must be ≪ realtime)");
                Console.WriteLine($"[selftest] STREAM FINAL: {last}");
            }
        }
        return string.IsNullOrWhiteSpace(raw) ? 1 : 0;
    }

    /// <summary>
    /// Exercises LlmClient against a local OpenAI-compatible endpoint (the mock server):
    /// registers a custom provider at 127.0.0.1:8899, runs non-streaming, streaming, and
    /// a tool-call round, and prints the results.
    /// </summary>
    private static async Task<int> SelfTestLlm(string[] args)
    {
        var idx = Array.IndexOf(args, "--selftest-llm");
        var baseUrl = args.Length > idx + 1 && !args[idx + 1].StartsWith("--") ? args[idx + 1] : "http://127.0.0.1:8899/v1";

        var provider = new Core.CustomProvider { Id = "mock", Name = "Mock", BaseUrl = baseUrl };
        Settings.Current.CustomProviders.Add(provider);
        Settings.Current.SelectedProviderID = "mock";
        Settings.Current.SelectedModelByProvider["mock"] = "mock-gpt";

        Console.WriteLine("[llm] models: " + string.Join(", ", await Ai.LlmClient.ListModelsAsync("mock", CancellationToken.None)));

        var messages = new List<Ai.LlmMessage>
        {
            new("system", Ai.PromptStore.CombineBasePrompt(PromptMode.Dictate, Ai.PromptStore.DictateDefaultBody)),
            new("user", "hello world this is a test of fluid voice dictation it works great"),
        };

        var nonStream = await Ai.LlmClient.CallAsync(new Ai.LlmRequest
        {
            ProviderId = "mock", Model = "mock-gpt", Messages = messages, Temperature = 0.2, Stream = false,
        }, CancellationToken.None);
        Console.WriteLine($"[llm] non-stream content: {nonStream.Content}");

        var streamed = new System.Text.StringBuilder();
        var stream = await Ai.LlmClient.CallAsync(new Ai.LlmRequest
        {
            ProviderId = "mock", Model = "mock-gpt", Messages = messages, Temperature = 0.2, Stream = true,
            OnContentDelta = s => streamed.Append(s),
        }, CancellationToken.None);
        Console.WriteLine($"[llm] streamed deltas: {streamed}");
        Console.WriteLine($"[llm] streamed final:  {stream.Content}");

        var toolResp = await Ai.LlmClient.CallAsync(new Ai.LlmRequest
        {
            ProviderId = "mock", Model = "mock-gpt",
            Messages = new List<Ai.LlmMessage> { new("system", CommandModeService.SystemPrompt), new("user", "what time is it") },
            Temperature = 0.1, Stream = false,
            Tools = new List<Ai.LlmTool>
            {
                new("execute_terminal_command", "Run a PowerShell command",
                    System.Text.Json.Nodes.JsonNode.Parse("""{"type":"object","properties":{"command":{"type":"string"}},"required":["command"]}""")!.AsObject()),
            },
        }, CancellationToken.None);
        Console.WriteLine($"[llm] tool calls: {toolResp.ToolCalls.Count} → {(toolResp.ToolCalls.Count > 0 ? toolResp.ToolCalls[0].Name + " " + toolResp.ToolCalls[0].ArgumentsJson : "none")}");

        var ok = !string.IsNullOrWhiteSpace(nonStream.Content) && streamed.Length > 0 && toolResp.ToolCalls.Count > 0;
        Console.WriteLine($"[llm] RESULT: {(ok ? "PASS" : "FAIL")}");
        return ok ? 0 : 1;
    }

    /// <summary>
    /// Types a fixed string after a countdown so you can focus a target window and see exactly
    /// what SendInput produces. --selftest-type [seconds] [text]
    /// </summary>
    private static int SelfTestType(string[] args)
    {
        var idx = Array.IndexOf(args, "--selftest-type");
        int seconds = args.Length > idx + 1 && int.TryParse(args[idx + 1], out var s) ? s : 4;
        if (args.Contains("--direct")) Settings.Current.TextInsertionMode = TextInsertionMode.Standard;
        var text = "Hello World, this is a test of fluid voice dictation. It works great!";
        Console.WriteLine($"[type] focus your target window; typing in {seconds}s…");
        Thread.Sleep(seconds * 1000);
        var target = FocusTracker.Capture();
        Console.WriteLine($"[type] target: {target?.ProcessName} — '{target?.WindowTitle}'");
        var ok = TypingService.TypeTextInstantly(text, target);
        Console.WriteLine($"[type] TypeTextInstantly returned {ok}; typed {text.Length} chars");
        Thread.Sleep(500);
        return ok ? 0 : 1;
    }

    private static float[] LoadWavAs16kMono(string path)
    {
        using var reader = new NAudio.Wave.AudioFileReader(path); // gives float samples
        var sourceRate = reader.WaveFormat.SampleRate;
        var channels = reader.WaveFormat.Channels;
        var all = new List<float>();
        var buffer = new float[sourceRate * channels];
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < read; i += channels)
            {
                float sum = 0;
                for (int c = 0; c < channels && i + c < read; c++) sum += buffer[i + c];
                all.Add(sum / channels);
            }
        }
        if (sourceRate == 16000) return all.ToArray();
        // simple linear resample
        var ratio = sourceRate / 16000.0;
        var outLen = (int)(all.Count / ratio);
        var output = new float[outLen];
        for (int i = 0; i < outLen; i++)
        {
            var pos = i * ratio;
            var i0 = (int)pos;
            var frac = (float)(pos - i0);
            var s0 = all[Math.Min(i0, all.Count - 1)];
            var s1 = all[Math.Min(i0 + 1, all.Count - 1)];
            output[i] = s0 + (s1 - s0) * frac;
        }
        return output;
    }
}
