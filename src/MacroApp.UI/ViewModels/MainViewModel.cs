using System.Collections.ObjectModel;
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
    private readonly MacroManager _macroManager;
    private readonly HotKeyManager _hotKeyManager;

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
        MacroManager macroManager,
        HotKeyManager hotKeyManager,
        MacroListViewModel macroList,
        EditorViewModel editor)
    {
        _hookManager = hookManager;
        _inputSimulator = inputSimulator;
        _recordingEngine = recordingEngine;
        _playbackEngine = playbackEngine;
        _macroManager = macroManager;
        _hotKeyManager = hotKeyManager;
        MacroList = macroList;
        Editor = editor;
        _dispatcher = Application.Current.Dispatcher;

        // Wire up engine events
        _recordingEngine.StateChanged += OnRecordingStateChanged;
        _recordingEngine.EventRecorded += OnEventRecorded;
        _recordingEngine.CountdownTick += OnCountdownTick;

        _playbackEngine.StateChanged += OnPlaybackStateChanged;
        _playbackEngine.Progress += OnPlaybackProgress;
        _playbackEngine.Completed += OnPlaybackCompleted;
        _playbackEngine.Error += OnPlaybackError;

        // Blink timer for recording indicator
        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AppConstants.RecordingBlinkIntervalMs) };
        _blinkTimer.Tick += (_, _) => RecordingIndicatorVisible = !RecordingIndicatorVisible;

        // Subscribe to macro selection changes
        MacroList.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MacroListViewModel.SelectedMacro))
            {
                SelectedMacro = MacroList.SelectedMacro;
                if (SelectedMacro != null)
                    Editor.LoadMacro(SelectedMacro);
            }
        };
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
            await _recordingEngine.StartAsync();
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
        var events = _recordingEngine.Stop();

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
        if (SelectedMacro == null || SelectedMacro.Events.Count == 0)
        {
            StatusText = "No macro selected or macro is empty";
            return;
        }

        var settings = SelectedMacro.PlaybackSettings.Clone();
        settings.SpeedMultiplier = SelectedSpeed;

        _playbackEngine.Start(SelectedMacro.Events, settings);
        StatusText = $"Playing '{SelectedMacro.Name}'...";
    }

    private bool CanPlay() => !IsRecording && !IsPlaying && SelectedMacro != null;

    [RelayCommand(CanExecute = nameof(CanStopPlayback))]
    private void StopPlayback()
    {
        _playbackEngine.Stop();
        StatusText = "Playback stopped";
    }

    private bool CanStopPlayback() => IsPlaying;

    [RelayCommand(CanExecute = nameof(CanPausePlayback))]
    private void PausePlayback()
    {
        if (_playbackEngine.State == PlaybackState.Playing)
        {
            _playbackEngine.Pause();
            StatusText = "Playback paused";
        }
        else if (_playbackEngine.State == PlaybackState.Paused)
        {
            _playbackEngine.Resume();
            StatusText = "Playback resumed";
        }
    }

    private bool CanPausePlayback() => IsPlaying;

    [RelayCommand]
    private void StepThrough()
    {
        if (_playbackEngine.State == PlaybackState.StepThrough)
        {
            _playbackEngine.StepNext();
        }
        else if (_playbackEngine.State == PlaybackState.Playing)
        {
            _playbackEngine.EnterStepMode();
            StatusText = "Step-through mode";
        }
    }

    [RelayCommand]
    private void EmergencyStop()
    {
        if (IsRecording) _recordingEngine.Stop();
        if (IsPlaying) _playbackEngine.Stop();
        StatusText = "Emergency stop activated";
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
        Editor.SyncToMacro(SelectedMacro);
        await _macroManager.SaveAsync(SelectedMacro);
        StatusText = $"Saved '{SelectedMacro.Name}'";
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
            StatusText = $"Playing event {args.CurrentEventIndex + 1}/{args.TotalEvents} (repeat {args.CurrentRepeat})";
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
        StopRecordingCommand.NotifyCanExecuteChanged();
        PauseRecordingCommand.NotifyCanExecuteChanged();
        PlayCommand.NotifyCanExecuteChanged();
        StopPlaybackCommand.NotifyCanExecuteChanged();
        PausePlaybackCommand.NotifyCanExecuteChanged();
    }
}
