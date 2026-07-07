using System.IO;
using FluidVoice.App;
using FluidVoice.Core;
using FluidVoice.Input;
using FluidVoice.Stt;
using FluidVoice.Text;
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
            Console.WriteLine("FluidVoice for Windows 1.6.2 (port of altic-dev/FluidVoice)");
            return 0;
        }
        if (args.Contains("--selftest-stt"))
            return SelfTestStt(args).GetAwaiter().GetResult();

        using var singleInstance = new Mutex(true, @"Local\FluidVoice.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            System.Windows.MessageBox.Show("FluidVoice is already running — look for the F icon in the system tray.",
                "FluidVoice", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return 0;
        }

        var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };

        var overlay = new OverlayWindow();
        var coordinator = new DictationCoordinator(app.Dispatcher, overlay);
        var mainWindow = new MainWindow();
        var tray = new TrayIcon();
        var hook = new KeyboardHook();
        var hotkeys = new HotkeyManager(hook, coordinator);

        Notifications.ShowHandler = (title, body) => tray.ShowBalloon(title, body);
        coordinator.RecordingStateChanged += recording =>
            app.Dispatcher.BeginInvoke(() => tray.UpdateStatus(recording));

        tray.OpenRequested += () => app.Dispatcher.BeginInvoke(() => ShowMain(mainWindow));
        tray.SettingsRequested += () => app.Dispatcher.BeginInvoke(() => ShowMain(mainWindow));
        tray.DictionaryRequested += () => app.Dispatcher.BeginInvoke(() => ShowMain(mainWindow));
        tray.CheckUpdatesRequested += () => app.Dispatcher.BeginInvoke(() =>
            Notifications.Show("Updates", "Update checking arrives with the installer build."));
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

        // First run: show the main window; afterwards start in the tray like the mac menu-bar app.
        if (!Settings.Current.OnboardingCompleted)
            mainWindow.Show();

        Log.Info("app", $"FluidVoice started (hotkey: {Settings.Current.PrimaryDictationShortcuts.FirstOrDefault()?.DisplayString})");
        app.Run();
        return 0;
    }

    private static void ShowMain(MainWindow window)
    {
        window.Show();
        if (window.WindowState == System.Windows.WindowState.Minimized)
            window.WindowState = System.Windows.WindowState.Normal;
        window.Activate();
    }

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
        Console.WriteLine($"[selftest] model: {model.Id} ({(model.IsDownloaded ? "cached" : "will download " + model.SizeDisplay)})");

        using var whisper = new Stt.WhisperEngine();
        var progress = new Progress<ModelPreparationProgress>(p =>
        {
            if (p.Phase == ModelPreparationPhase.Downloading)
                Console.Write($"\r[selftest] downloading {(int)(p.Fraction * 100)}%   ");
        });
        await whisper.PrepareAsync(model, progress, CancellationToken.None);
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
        var raw = await whisper.TranscribeAsync(pcm, CancellationToken.None);
        sw.Stop();
        Console.WriteLine($"[selftest] transcribed in {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"[selftest] RAW: {raw}");
        var formatted = TranscriptFormatter.Process(raw);
        Console.WriteLine($"[selftest] FORMATTED: {formatted}");
        return string.IsNullOrWhiteSpace(raw) ? 1 : 0;
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
