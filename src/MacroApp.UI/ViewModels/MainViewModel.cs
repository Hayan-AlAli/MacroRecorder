using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacroApp.Core;
using MacroApp.Core.Models;
using MacroApp.Core.Playback;
using MacroApp.Core.Recording;
using MacroApp.NativeInterop;

namespace MacroApp.UI.ViewModels;

/// <summary>
/// Central ViewModel for the main window. Orchestrates recording, playback, and macro management.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly HookManager _hookManager;
    private readonly InputSimulator _inputSimulator;
    private readonly RecordingEngine _recordingEngine;
    private readonly PlaybackEngine _playbackEngine;
    private readonly ScriptPlaybackEngine _scriptPlaybackEngine;
    private readonly MacroManager _macroManager;
    private readonly HotKeyManager _hotKeyManager;

    // Whichever engine is running right now (script or raw events)
    private IPlaybackEngine? _activePlayback;
    private CancellationTokenSource? _countdownCts;
    private int _emergencyStopKey = AppConstants.DefaultEmergencyStopKey;
    private readonly Dictionary<int, Action> _hotKeyActions = new();

    // ── Observable State ────────────────────────────────────────────

    [ObservableProperty]
    private string _appState = "Idle";

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private bool _isCountingDown;

    [ObservableProperty]
    private int _countdownValue;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private int _eventCount;

    [ObservableProperty]
    private int _currentPlaybackIndex;

    [ObservableProperty]
    private int _currentRepeat;

    [ObservableProperty]
    private double _selectedSpeed = AppConstants.DefaultSpeedMultiplier;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    private Macro? _selectedMacro;

    [ObservableProperty]
    private bool _recordingIndicatorVisible;

    /// <summary>
    /// Available speed multiplier options.
    /// </summary>
    public ObservableCollection<double> SpeedOptions { get; } = new(AppConstants.SpeedMultipliers);

    /// <summary>
    /// Live event list for the current recording.
    /// </summary>
    public ObservableCollection<InputEvent> LiveEvents { get; } = new();

    /// <summary>
    /// Reference to the macro list ViewModel.
    /// </summary>
    public MacroListViewModel MacroList { get; }

    /// <summary>
    /// Reference to the editor ViewModel.
    /// </summary>
    public EditorViewModel Editor { get; }

    private DispatcherTimer? _blinkTimer;
    private readonly Dispatcher _dispatcher;

    public MainViewModel(
        HookManager hookManager,
        InputSimulator inputSimulator,
        RecordingEngine recordingEngine,
        PlaybackEngine playbackEngine,
        ScriptPlaybackEngine scriptPlaybackEngine,
        MacroManager macroManager,
        HotKeyManager hotKeyManager,
        MacroListViewModel macroList,
        EditorViewModel editor)
    {
        _hookManager = hookManager;
        _inputSimulator = inputSimulator;
        _recordingEngine = recordingEngine;
        _playbackEngine = playbackEngine;
        _scriptPlaybackEngine = scriptPlaybackEngine;
        _macroManager = macroManager;
        _hotKeyManager = hotKeyManager;
        MacroList = macroList;
        Editor = editor;
        _dispatcher = Application.Current.Dispatcher;

        // Wire up engine events
        _recordingEngine.StateChanged += OnRecordingStateChanged;
        _recordingEngine.EventRecorded += OnEventRecorded;
        _recordingEngine.CountdownTick += OnCountdownTick;

        foreach (IPlaybackEngine engine in new IPlaybackEngine[] { _playbackEngine, _scriptPlaybackEngine })
        {
            engine.StateChanged += OnPlaybackStateChanged;
            engine.Progress += OnPlaybackProgress;
            engine.Completed += OnPlaybackCompleted;
            engine.Error += OnPlaybackError;
        }

        // Emergency stop has to work while another app has focus, so it listens on the
        // global keyboard hook. Injected keys are skipped so a macro can't stop itself.
        _hookManager.InputEvents
            .Where(e => !e.IsInjected
                        && (e.EventType is RawInputEventType.KeyDown or RawInputEventType.SysKeyDown)
                        && e.VirtualKeyCode == _emergencyStopKey)
            .Subscribe(_ => _dispatcher.BeginInvoke(() =>
            {
                if (IsPlaying) EmergencyStop();
            }));

        _hotKeyManager.HotKeyTriggered.Subscribe(id =>
        {
            if (_hotKeyActions.TryGetValue(id, out var action))
                _dispatcher.BeginInvoke(action);
        });

        // Blink timer for recording indicator
        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AppConstants.RecordingBlinkIntervalMs) };
        _blinkTimer.Tick += (_, _) => RecordingIndicatorVisible = !RecordingIndicatorVisible;

        // Subscribe to macro selection changes
        MacroList.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MacroListViewModel.SelectedMacro))
            {
                // Don't throw away unsaved edits when the user clicks another macro
                FlushEditor(save: true);

                SelectedMacro = MacroList.SelectedMacro;
                if (SelectedMacro != null)
                    Editor.LoadMacro(SelectedMacro);
            }
        };
    }

    /// <summary>
    /// Hooks the app up to its main window: registers the global hotkeys and keeps clicks
    /// on the window itself out of recordings. Call once the window has a handle.
    /// </summary>
    public void AttachToWindow(IntPtr hwnd)
    {
        _recordingEngine.IgnoredWindow = hwnd;
        _hotKeyManager.SetWindowHandle(hwnd);

        var failed = new List<string>();
        RegisterHotKey(AppConstants.DefaultRecordKey, ToggleRecording, failed);
        RegisterHotKey(AppConstants.DefaultPlayKey, TogglePlayback, failed);
        RegisterHotKey(AppConstants.DefaultStopKey, StopEverything, failed);

        if (failed.Count > 0)
            StatusText = $"Couldn't register {string.Join(", ", failed)} — another program is probably using them";
    }

    private void RegisterHotKey(int virtualKey, Action action, List<string> failed)
    {
        int id = _hotKeyManager.Register(new HotKeyBinding(0, (uint)virtualKey));
        if (id < 0)
        {
            failed.Add(KeyNames.Format(virtualKey));
            return;
        }

        _hotKeyActions[id] = action;

        // The hotkeys still reach the low-level hook, so keep them out of recordings
        if (!_recordingEngine.Settings.ExcludedKeys.Contains(virtualKey))
            _recordingEngine.Settings.ExcludedKeys.Add(virtualKey);
    }

    private void ToggleRecording()
    {
        if (IsRecording || IsCountingDown)
            StopRecordingCommand.Execute(null);
        else if (RecordCommand.CanExecute(null))
            RecordCommand.Execute(null);
    }

    private void TogglePlayback()
    {
        if (IsPlaying)
            StopPlayback();
        else if (PlayCommand.CanExecute(null))
            Play();
    }

    private void StopEverything()
    {
        if (IsRecording || IsCountingDown)
            StopRecordingCommand.Execute(null);
        else if (IsPlaying)
            StopPlayback();
    }

    /// <summary>
    /// Copies pending editor changes into the selected macro, optionally saving it to disk.
    /// </summary>
    public void FlushEditor(bool save)
    {
        var macro = SelectedMacro;
        if (macro == null || !Editor.IsDirty) return;

        Editor.SyncToMacro(macro);
        Editor.IsDirty = false;

        if (save)
            _ = SaveQuietlyAsync(macro);
    }

    private async Task SaveQuietlyAsync(Macro macro)
    {
        try
        {
            await _macroManager.SaveAsync(macro);
        }
        catch (Exception ex)
        {
            App.Logger.Error(ex, "Failed to save macro {Name}", macro.Name);
            StatusText = $"Couldn't save '{macro.Name}': {ex.Message}";
        }
    }

    /// <summary>
    /// Initializes the ViewModel — loads macros from disk.
    /// </summary>
    [RelayCommand]
    private async Task InitializeAsync()
    {
        StatusText = "Loading macros...";
        await _macroManager.LoadAllAsync();
        MacroList.RefreshFromManager(_macroManager);
        StatusText = $"Loaded {_macroManager.Macros.Count} macros";
    }

    // ── Recording Commands ──────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanRecord))]
    private async Task RecordAsync()
    {
        try
        {
            StatusText = "Starting recording...";
            _countdownCts?.Dispose();
            _countdownCts = new CancellationTokenSource();
            await _recordingEngine.StartAsync(_countdownCts.Token);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Recording cancelled";
        }
        catch (Exception ex)
        {
            StatusText = $"Recording failed: {ex.Message}";
            App.Logger.Error(ex, "Recording failed");
        }
    }

    private bool CanRecord() => !IsRecording && !IsPlaying;

    [RelayCommand(CanExecute = nameof(CanStopRecording))]
    private async Task StopRecordingAsync()
    {
        if (_recordingEngine.State == RecordingState.Countdown)
        {
            _countdownCts?.Cancel();
            return;
        }

        var events = _recordingEngine.Stop();

        // Commit any pending edits first so the list refresh below can't write them over the new recording
        FlushEditor(save: false);

        if (events.Count > 0)
        {
            // Create or update the selected macro
            Macro macro;
            if (SelectedMacro != null)
            {
                macro = SelectedMacro;
                macro.Events = events;
                macro.MarkModified();
            }
            else
            {
                macro = await _macroManager.CreateAsync($"Recording_{DateTime.Now:yyyyMMdd_HHmmss}");
                macro.Events = events;
            }

            // Generate script text from events
            macro.ScriptText = string.Join("\n", events.Select(e => e.ToScriptLine()));

            await _macroManager.SaveAsync(macro);
            MacroList.RefreshFromManager(_macroManager);
            MacroList.SelectedMacro = macro;
            SelectedMacro = macro;
            Editor.LoadMacro(macro);

            StatusText = $"Recorded {events.Count} events";
        }
        else
        {
            StatusText = "Recording stopped (no events)";
        }
    }

    private bool CanStopRecording() => IsRecording || IsCountingDown;

    [RelayCommand(CanExecute = nameof(CanPauseRecording))]
    private void PauseRecording()
    {
        if (_recordingEngine.State == RecordingState.Recording)
        {
            _recordingEngine.Pause();
            StatusText = "Recording paused";
        }
        else if (_recordingEngine.State == RecordingState.Paused)
        {
            _recordingEngine.Resume();
            StatusText = "Recording resumed";
        }
    }

    private bool CanPauseRecording() => IsRecording;

    // ── Playback Commands ───────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void Play()
    {
        var macro = SelectedMacro;
        if (macro == null)
        {
            StatusText = "Select a macro first";
            return;
        }

        if (_playbackEngine.State != PlaybackState.Idle || _scriptPlaybackEngine.State != PlaybackState.Idle)
            return;

        var settings = macro.PlaybackSettings.Clone();
        settings.SpeedMultiplier = SelectedSpeed;
        settings.RepeatCount = Editor.RepeatCount;

        EnsureEmergencyStopHook(settings.EmergencyStopKey);

        try
        {
            // The script is the source of truth once there is one: it's generated from the
            // recording and then edited, so it's what the user expects to run.
            if (!string.IsNullOrWhiteSpace(Editor.ScriptText))
            {
                _activePlayback = _scriptPlaybackEngine;
                _scriptPlaybackEngine.Start(Editor.ScriptText, settings);
            }
            else if (macro.Events.Count > 0)
            {
                _activePlayback = _playbackEngine;
                _playbackEngine.Start(macro.Events, settings);
            }
            else
            {
                StatusText = "This macro is empty — record something or write a script first";
                return;
            }
        }
        catch (InvalidOperationException ex)
        {
            _activePlayback = null;
            StatusText = $"Can't play: {ex.Message}";
            return;
        }

        StatusText = $"Playing '{macro.Name}'... (press {KeyNames.Format(_emergencyStopKey)} to stop)";
    }

    private void EnsureEmergencyStopHook(int stopKey)
    {
        _emergencyStopKey = stopKey;
        if (_hookManager.IsRunning) return;

        try
        {
            _hookManager.Start();
        }
        catch (Exception ex)
        {
            App.Logger.Warning(ex, "Couldn't start the input hook; emergency stop only works while the window is focused");
        }
    }

    private bool CanPlay() => !IsRecording && !IsPlaying && SelectedMacro != null;

    [RelayCommand(CanExecute = nameof(CanStopPlayback))]
    private void StopPlayback()
    {
        _activePlayback?.Stop();
        StatusText = "Playback stopped";
    }

    private bool CanStopPlayback() => IsPlaying;

    [RelayCommand(CanExecute = nameof(CanPausePlayback))]
    private void PausePlayback()
    {
        if (_activePlayback?.State == PlaybackState.Playing)
        {
            _activePlayback.Pause();
            StatusText = "Playback paused";
        }
        else if (_activePlayback?.State is PlaybackState.Paused or PlaybackState.StepThrough)
        {
            _activePlayback.Resume();
            StatusText = "Playback resumed";
        }
    }

    private bool CanPausePlayback() => IsPlaying;

    [RelayCommand]
    private void StepThrough()
    {
        if (_activePlayback?.State == PlaybackState.StepThrough)
        {
            _activePlayback.StepNext();
        }
        else if (_activePlayback?.State is PlaybackState.Playing or PlaybackState.Paused)
        {
            _activePlayback.EnterStepMode();
            StatusText = "Step-through mode — press Step to run the next line";
        }
    }

    [RelayCommand]
    private void EmergencyStop()
    {
        if (IsPlaying)
        {
            _activePlayback?.Stop();
            StatusText = "Emergency stop — playback halted";
        }
        else if (IsRecording || IsCountingDown)
        {
            // Keep what was recorded; Esc in the app window is a stop, not a discard
            StopRecordingCommand.Execute(null);
        }
    }

    // ── Macro Commands ──────────────────────────────────────────────

    [RelayCommand]
    private async Task NewMacroAsync()
    {
        var macro = await _macroManager.CreateAsync();
        MacroList.RefreshFromManager(_macroManager);
        MacroList.SelectedMacro = macro;
    }

    [RelayCommand]
    private async Task SaveMacroAsync()
    {
        if (SelectedMacro == null) return;
        var macro = SelectedMacro;
        Editor.SyncToMacro(macro);
        await _macroManager.SaveAsync(macro);
        Editor.IsDirty = false;
        StatusText = $"Saved '{macro.Name}'";
    }

    // ── Engine Event Handlers ───────────────────────────────────────

    private void OnRecordingStateChanged(RecordingState state)
    {
        _dispatcher.BeginInvoke(() =>
        {
            IsRecording = state == RecordingState.Recording || state == RecordingState.Paused;
            IsCountingDown = state == RecordingState.Countdown;
            IsPaused = state == RecordingState.Paused;
            AppState = state.ToString();

            if (state == RecordingState.Recording)
            {
                _blinkTimer?.Start();
                LiveEvents.Clear();
                StatusText = "Recording...";
            }
            else
            {
                _blinkTimer?.Stop();
                RecordingIndicatorVisible = false;
            }

            RefreshCommands();
        });
    }

    private void OnEventRecorded(InputEvent evt)
    {
        _dispatcher.BeginInvoke(() =>
        {
            LiveEvents.Add(evt);
            EventCount = LiveEvents.Count;
        });
    }

    private void OnCountdownTick(int secondsRemaining)
    {
        _dispatcher.BeginInvoke(() =>
        {
            CountdownValue = secondsRemaining;
            StatusText = $"Recording in {secondsRemaining}...";
        });
    }

    private void OnPlaybackStateChanged(PlaybackState state)
    {
        _dispatcher.BeginInvoke(() =>
        {
            IsPlaying = state is PlaybackState.Playing or PlaybackState.Paused or PlaybackState.StepThrough;
            IsPaused = state == PlaybackState.Paused;
            AppState = state.ToString();
            RefreshCommands();
        });
    }

    private void OnPlaybackProgress(PlaybackProgressEventArgs args)
    {
        _dispatcher.BeginInvoke(() =>
        {
            CurrentPlaybackIndex = args.CurrentEventIndex;
            CurrentRepeat = args.CurrentRepeat;
            string repeat = args.TotalRepeats < 0 ? $"{args.CurrentRepeat}/∞" : $"{args.CurrentRepeat}/{args.TotalRepeats}";
            StatusText = args.Line > 0
                ? $"Running line {args.Line} (repeat {repeat})"
                : $"Playing event {args.CurrentEventIndex + 1}/{args.TotalEvents} (repeat {repeat})";
        });
    }

    private void OnPlaybackCompleted()
    {
        _dispatcher.BeginInvoke(() =>
        {
            StatusText = "Playback completed";
            IsPlaying = false;
        });
    }

    private void OnPlaybackError(Exception ex)
    {
        _dispatcher.BeginInvoke(() =>
        {
            StatusText = $"Playback error: {ex.Message}";
            App.Logger.Error(ex, "Playback error");
        });
    }

    private void RefreshCommands()
    {
        RecordCommand.NotifyCanExecuteChanged();
        StepThroughCommand.NotifyCanExecuteChanged();
        StopRecordingCommand.NotifyCanExecuteChanged();
        PauseRecordingCommand.NotifyCanExecuteChanged();
        PlayCommand.NotifyCanExecuteChanged();
        StopPlaybackCommand.NotifyCanExecuteChanged();
        PausePlaybackCommand.NotifyCanExecuteChanged();
    }
}
