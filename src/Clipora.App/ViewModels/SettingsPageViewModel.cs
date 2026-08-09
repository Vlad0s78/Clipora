using System.Globalization;
using Clipora.App.Services;
using Clipora.Core;
using Clipora.Core.Interfaces;
using Clipora.Core.Models;
using Clipora.Core.Services;
using Clipora.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Clipora.App.ViewModels;

public sealed partial class SettingsPageViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly IOutputFolderPicker _folderPicker;
    private readonly ILogFolderLauncher _logFolderLauncher;
    private readonly ITrimShortcutService _trimShortcutService;
    private readonly IExplorerIntegrationService _explorerIntegrationService;
    private readonly AppDataPaths _dataPaths;
    private readonly ResourceLoader _resources;
    private readonly ILogger<SettingsPageViewModel> _logger;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private AppSettings _loadedSettings = new();
    private TrimShortcutBindings _shortcutBindings = TrimShortcutBindings.Default;
    private TrimShortcutAction? _recordingShortcutAction;
    private TrimShortcutValidationError _shortcutInputError;

    [ObservableProperty]
    public partial int SelectedLanguageIndex { get; set; }

    [ObservableProperty]
    public partial int SelectedOutputModeIndex { get; set; }

    [ObservableProperty]
    public partial string CustomOutputFolder { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShowTechnicalLog { get; set; }

    [ObservableProperty]
    public partial bool ExplorerIntegration { get; set; }

    [ObservableProperty]
    public partial string ValidationMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool CanSave { get; set; } = true;

    [ObservableProperty]
    public partial string ShortcutValidationMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ShortcutRecorderStatusMessage { get; set; } = string.Empty;

    public SettingsPageViewModel(
        ISettingsService settingsService,
        IOutputFolderPicker folderPicker,
        ILogFolderLauncher logFolderLauncher,
        ITrimShortcutService trimShortcutService,
        IExplorerIntegrationService explorerIntegrationService,
        AppDataPaths dataPaths,
        ResourceLoader resources,
        ILogger<SettingsPageViewModel> logger)
    {
        _settingsService = settingsService;
        _folderPicker = folderPicker;
        _logFolderLauncher = logFolderLauncher;
        _trimShortcutService = trimShortcutService;
        _explorerIntegrationService = explorerIntegrationService;
        _dataPaths = dataPaths;
        _resources = resources;
        _logger = logger;
    }

    public string LogDirectory => _logFolderLauncher.LogDirectory;

    public string CustomOutputFolderDisplay => string.IsNullOrWhiteSpace(CustomOutputFolder)
        ? GetString("SettingsNoCustomFolder")
        : CustomOutputFolder;

    public bool IsCustomFolder => SelectedOutputModeIndex == (int)OutputMode.CustomFolder;

    public bool IsExplorerIntegrationAvailable => !_dataPaths.IsPortable;

    public Visibility PortableExplorerNoticeVisibility => _dataPaths.IsPortable
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility TechnicalLogVisibility => ShowTechnicalLog
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string PlayPauseShortcutDisplay => GetShortcutDisplay(TrimShortcutAction.PlayPause);

    public string SetStartShortcutDisplay => GetShortcutDisplay(TrimShortcutAction.SetStart);

    public string SetEndShortcutDisplay => GetShortcutDisplay(TrimShortcutAction.SetEnd);

    public bool IsRecordingShortcut => _recordingShortcutAction.HasValue;

    public TrimShortcutAction? RecordingShortcutAction => _recordingShortcutAction;

    public async Task LoadAsync()
    {
        try
        {
            _loadedSettings = await _settingsService.LoadAsync(_lifetimeCancellation.Token);
            string effectiveLanguage = ResolveLanguage(_loadedSettings);
            SelectedLanguageIndex = effectiveLanguage == LanguagePolicy.Russian ? 0 : 1;
            SelectedOutputModeIndex = (int)_loadedSettings.OutputMode;
            CustomOutputFolder = _loadedSettings.CustomOutputFolder ?? string.Empty;
            ShowTechnicalLog = _loadedSettings.ShowTechnicalLog;
            ExplorerIntegration = IsExplorerIntegrationAvailable && ReadExplorerIntegrationState();
            _shortcutBindings = TrimShortcutPolicy.NormalizeOrDefault(_loadedSettings.TrimShortcuts);
            _recordingShortcutAction = null;
            _shortcutInputError = TrimShortcutValidationError.None;
            ShortcutRecorderStatusMessage = string.Empty;
            StatusMessage = string.Empty;
            RaiseShortcutPropertiesChanged();
            UpdateValidation();
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
    }

    partial void OnSelectedOutputModeIndexChanged(int value)
    {
        StatusMessage = string.Empty;
        OnPropertyChanged(nameof(IsCustomFolder));
        UpdateValidation();
    }

    partial void OnSelectedLanguageIndexChanged(int value)
    {
        StatusMessage = string.Empty;
    }

    partial void OnCustomOutputFolderChanged(string value)
    {
        StatusMessage = string.Empty;
        OnPropertyChanged(nameof(CustomOutputFolderDisplay));
        UpdateValidation();
    }

    partial void OnShowTechnicalLogChanged(bool value)
    {
        StatusMessage = string.Empty;
        OnPropertyChanged(nameof(TechnicalLogVisibility));
    }

    partial void OnExplorerIntegrationChanged(bool value)
    {
        StatusMessage = string.Empty;
    }

    public void BeginShortcutRecording(TrimShortcutAction action)
    {
        _recordingShortcutAction = action;
        _shortcutInputError = TrimShortcutValidationError.None;
        StatusMessage = string.Empty;
        ShortcutValidationMessage = string.Empty;
        ShortcutRecorderStatusMessage = GetString("SettingsShortcutRecordingPrompt");
        RaiseShortcutPropertiesChanged();
        UpdateValidation();
    }

    public void CancelShortcutRecording()
    {
        if (!_recordingShortcutAction.HasValue)
        {
            return;
        }

        _recordingShortcutAction = null;
        _shortcutInputError = TrimShortcutValidationError.None;
        ShortcutRecorderStatusMessage = string.Empty;
        RaiseShortcutPropertiesChanged();
        UpdateValidation();
    }

    public bool TryCompleteShortcutRecording(ShortcutGesture gesture)
    {
        if (_recordingShortcutAction is not TrimShortcutAction action)
        {
            return false;
        }

        if (!TrimShortcutPolicy.IsAssignable(gesture, out TrimShortcutValidationError error))
        {
            ReportShortcutRecordingError(error);
            return false;
        }

        _shortcutBindings = action switch
        {
            TrimShortcutAction.PlayPause => _shortcutBindings with { PlayPause = gesture },
            TrimShortcutAction.SetStart => _shortcutBindings with { SetStart = gesture },
            TrimShortcutAction.SetEnd => _shortcutBindings with { SetEnd = gesture },
            _ => _shortcutBindings,
        };
        _recordingShortcutAction = null;
        _shortcutInputError = TrimShortcutValidationError.None;
        ShortcutRecorderStatusMessage = string.Empty;
        StatusMessage = string.Empty;
        RaiseShortcutPropertiesChanged();
        UpdateValidation();
        return string.IsNullOrEmpty(ShortcutValidationMessage);
    }

    public void ReportShortcutRecordingError(TrimShortcutValidationError error)
    {
        _recordingShortcutAction = null;
        _shortcutInputError = error is TrimShortcutValidationError.None
            ? TrimShortcutValidationError.UnsupportedKey
            : error;
        ShortcutRecorderStatusMessage = string.Empty;
        StatusMessage = string.Empty;
        RaiseShortcutPropertiesChanged();
        UpdateValidation();
    }

    [RelayCommand]
    private void RestoreDefaultShortcuts()
    {
        _shortcutBindings = TrimShortcutBindings.Default;
        _recordingShortcutAction = null;
        _shortcutInputError = TrimShortcutValidationError.None;
        StatusMessage = string.Empty;
        ShortcutRecorderStatusMessage = GetString("SettingsShortcutDefaultsRestored");
        RaiseShortcutPropertiesChanged();
        UpdateValidation();
    }

    [RelayCommand]
    private async Task ChooseOutputFolderAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);

        try
        {
            string? selectedFolder = await _folderPicker.PickAsync(linked.Token);
            if (selectedFolder is not null)
            {
                CustomOutputFolder = selectedFolder;
                StatusMessage = string.Empty;
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Не удалось выбрать папку для результата.");
            StatusMessage = GetString("SettingsFolderPickerError");
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync(CancellationToken cancellationToken)
    {
        UpdateValidation();
        if (!CanSave)
        {
            return;
        }

        if (!TrimShortcutPolicy.TryNormalize(
                _shortcutBindings,
                out TrimShortcutBindings normalizedShortcuts,
                out TrimShortcutValidationError shortcutError))
        {
            _shortcutInputError = shortcutError;
            UpdateValidation();
            return;
        }

        string? customFolder = string.IsNullOrWhiteSpace(CustomOutputFolder)
            ? null
            : CustomOutputFolder;
        if (IsCustomFolder &&
            OutputDirectoryPolicy.TryNormalizeExistingDirectory(CustomOutputFolder, out string? normalizedFolder))
        {
            customFolder = normalizedFolder;
        }

        string language = SelectedLanguageIndex == 0 ? LanguagePolicy.Russian : LanguagePolicy.English;
        AppSettings settings = _loadedSettings with
        {
            Language = language,
            OutputMode = Enum.IsDefined((OutputMode)SelectedOutputModeIndex)
                ? (OutputMode)SelectedOutputModeIndex
                : OutputMode.SameFolder,
            CustomOutputFolder = customFolder,
            ExplorerIntegration = IsExplorerIntegrationAvailable && ExplorerIntegration,
            ShowTechnicalLog = ShowTechnicalLog,
            TrimShortcuts = normalizedShortcuts,
        };

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);
        bool updateExplorerIntegration = IsExplorerIntegrationAvailable &&
            ExplorerIntegration != _loadedSettings.ExplorerIntegration ||
            IsExplorerIntegrationAvailable && ExplorerIntegration && !string.Equals(
                language,
                ResolveLanguage(_loadedSettings),
                StringComparison.OrdinalIgnoreCase);
        bool settingsSaved = false;
        try
        {
            await _settingsService.SaveAsync(settings, linked.Token);
            settingsSaved = true;
            if (updateExplorerIntegration)
            {
                _explorerIntegrationService.SetEnabled(
                    ExplorerIntegration,
                    GetExecutablePath(),
                    language);
            }

            _loadedSettings = settings;
            _shortcutBindings = normalizedShortcuts;
            if (!_trimShortcutService.TryApply(normalizedShortcuts, out TrimShortcutValidationError applyError))
            {
                _shortcutInputError = applyError;
                ShortcutValidationMessage = applyError is TrimShortcutValidationError.None
                    ? GetString("SettingsShortcutApplyError")
                    : GetShortcutValidationMessage(applyError);
                CanSave = false;
                StatusMessage = GetString("SettingsShortcutApplyError");
                _logger.LogError("Не удалось применить сочетания клавиш после сохранения: {ShortcutError}.", applyError);
                return;
            }

            _shortcutInputError = TrimShortcutValidationError.None;
            ShortcutRecorderStatusMessage = string.Empty;
            RaiseShortcutPropertiesChanged();
            UpdateValidation();
            StatusMessage = GetString("SettingsSavedMessage");
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (settingsSaved)
            {
                await RestorePreviousExplorerSettingsAsync();
            }

            ExplorerIntegration = ReadExplorerIntegrationState();
            _logger.LogError(exception, "Не удалось сохранить настройки.");
            StatusMessage = GetString("SettingsSaveError");
        }
    }

    [RelayCommand]
    private async Task OpenLogFolderAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);

        try
        {
            bool opened = await _logFolderLauncher.OpenAsync(linked.Token);
            if (!opened)
            {
                StatusMessage = GetString("SettingsLogFolderOpenError");
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Не удалось открыть папку журналов.");
            StatusMessage = GetString("SettingsLogFolderOpenError");
        }
    }

    private void UpdateValidation()
    {
        bool outputValid = true;
        if (!IsCustomFolder)
        {
            ValidationMessage = string.Empty;
        }
        else
        {
            outputValid = OutputDirectoryPolicy.TryNormalizeExistingDirectory(
                CustomOutputFolder,
                out _);
            ValidationMessage = outputValid ? string.Empty : GetString("SettingsCustomFolderRequired");
        }

        bool shortcutsValid;
        if (_recordingShortcutAction.HasValue)
        {
            shortcutsValid = false;
            ShortcutValidationMessage = string.Empty;
        }
        else if (_shortcutInputError is not TrimShortcutValidationError.None)
        {
            shortcutsValid = false;
            ShortcutValidationMessage = GetShortcutValidationMessage(_shortcutInputError);
        }
        else
        {
            shortcutsValid = TrimShortcutPolicy.TryNormalize(
                _shortcutBindings,
                out _,
                out TrimShortcutValidationError shortcutError);
            ShortcutValidationMessage = shortcutsValid
                ? string.Empty
                : GetShortcutValidationMessage(shortcutError);
        }

        CanSave = outputValid && shortcutsValid;
    }

    private string GetShortcutDisplay(TrimShortcutAction action)
    {
        return _recordingShortcutAction == action
            ? GetString("SettingsShortcutRecordingButton")
            : _shortcutBindings.Get(action).ToDisplayString();
    }

    private bool ReadExplorerIntegrationState()
    {
        if (!IsExplorerIntegrationAvailable)
        {
            return false;
        }

        try
        {
            return _explorerIntegrationService.IsEnabled(GetExecutablePath());
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Не удалось прочитать состояние интеграции с Проводником.");
            return false;
        }
    }

    private async Task RestorePreviousExplorerSettingsAsync()
    {
        try
        {
            await _settingsService.SaveAsync(_loadedSettings, CancellationToken.None);
            if (IsExplorerIntegrationAvailable)
            {
                _explorerIntegrationService.SetEnabled(
                    _loadedSettings.ExplorerIntegration,
                    GetExecutablePath(),
                    ResolveLanguage(_loadedSettings));
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Не удалось откатить настройки интеграции с Проводником.");
        }
    }

    private static string ResolveLanguage(AppSettings settings)
    {
        return settings.Language
            ?? (CultureInfo.CurrentUICulture.Name.StartsWith("ru", StringComparison.OrdinalIgnoreCase)
                ? LanguagePolicy.Russian
                : LanguagePolicy.English);
    }

    private static string GetExecutablePath()
    {
        return Environment.ProcessPath
            ?? throw new InvalidOperationException("Clipora executable path is unavailable.");
    }

    private string GetShortcutValidationMessage(TrimShortcutValidationError error)
    {
        string key = error switch
        {
            TrimShortcutValidationError.MissingGesture => "SettingsShortcutMissingError",
            TrimShortcutValidationError.UnsupportedKey => "SettingsShortcutUnsupportedKeyError",
            TrimShortcutValidationError.UnassignableKey => "SettingsShortcutUnassignableKeyError",
            TrimShortcutValidationError.UnsupportedModifiers => "SettingsShortcutUnsupportedModifiersError",
            TrimShortcutValidationError.ReservedModifier => "SettingsShortcutReservedModifierError",
            TrimShortcutValidationError.ReservedGesture => "SettingsShortcutReservedGestureError",
            TrimShortcutValidationError.DuplicateGesture => "SettingsShortcutDuplicateError",
            _ => "SettingsShortcutUnsupportedKeyError",
        };
        return GetString(key);
    }

    private void RaiseShortcutPropertiesChanged()
    {
        OnPropertyChanged(nameof(PlayPauseShortcutDisplay));
        OnPropertyChanged(nameof(SetStartShortcutDisplay));
        OnPropertyChanged(nameof(SetEndShortcutDisplay));
        OnPropertyChanged(nameof(IsRecordingShortcut));
        OnPropertyChanged(nameof(RecordingShortcutAction));
    }

    private string GetString(string key)
    {
        return _resources.GetString(key);
    }

    public void Dispose()
    {
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
    }
}
