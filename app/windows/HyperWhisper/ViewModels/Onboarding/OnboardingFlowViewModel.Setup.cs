using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyperWhisper.Localization;
using HyperWhisper.Models;
using HyperWhisper.ViewModels.Base;

namespace HyperWhisper.ViewModels.Onboarding;

public sealed partial class OnboardingFlowViewModel : ViewModelBase
{
    // =========================================================================
    // SETUP STEP
    // =========================================================================

    private string? _setupErrorMessage;

    /// <summary>
    /// Bug 2: the single error surface for the setup step, fed by Whisper AND
    /// Parakeet download failures, licence activation failures, and credential write
    /// failures, whichever matches the selected source.
    /// </summary>
    public string? SetupErrorMessage
    {
        get => _setupErrorMessage;
        private set
        {
            if (SetProperty(ref _setupErrorMessage, value))
                OnPropertyChanged(nameof(HasSetupError));
        }
    }

    public bool HasSetupError => !string.IsNullOrEmpty(SetupErrorMessage);

    /// <summary>The curated on-device shortlist, resolved live from the catalog.</summary>
    public IReadOnlyList<OnboardingModelSelection> AvailableModels => _catalog.Models;

    public bool IsInstalled(OnboardingModelSelection model) => _catalog.IsInstalled(model);

    public bool IsSelectedModelInstalled => SelectedModel is not null && _catalog.IsInstalled(SelectedModel);

    public bool IsSelectedModelDownloading => SelectedModel is not null && _catalog.IsDownloading(SelectedModel);

    public double SelectedModelProgress => SelectedModel is null ? 0 : _catalog.Progress(SelectedModel);

    [RelayCommand]
    public void StartSelectedModelDownload()
    {
        if (!_isLive || SelectedModel is not { } model)
            return;

        _catalog.StartDownload(model);
        RefreshSetupError();
        OnDownloadActivity(this, EventArgs.Empty);
    }

    /// <summary>
    /// The Setup step's "Save API key".
    ///
    /// It used to capture a restore point and persist WITHOUT probing, and recorded
    /// nothing in the per-session validation table - so it overwrote the user's real
    /// Credential Manager entry with an unverified key and still could not make
    /// <see cref="IsSelectedSourceUsable"/> true. The button renders exactly when
    /// that property is false, so pressing it was a dead end that cost the user their
    /// stored credential.
    ///
    /// It is now the same probe-then-persist action as "Test API key": the write only
    /// happens once the provider has accepted the key, and when it happens it opens
    /// the gate. A rejected or unreachable provider leaves the stored key untouched
    /// and puts the reason on the step's error line.
    /// </summary>
    [RelayCommand]
    public void SaveProviderKey() => ProbeAndPersistProviderKey();

    /// <summary>
    /// One error property for the setup step, per selected source. The on-device
    /// branch reads whichever engine the SELECTED model belongs to, which is what
    /// makes Parakeet failures visible.
    ///
    /// Everything here is produced INSIDE this flow. The licence manager's last error
    /// and the API key service's global validation state are app-wide, long-lived and
    /// unobserved, so falling back to them would render an unrelated failure from an
    /// earlier session before the user had done anything on the step. A credits fetch
    /// failure is not part of this funnel either.
    /// </summary>
    private void RefreshSetupError()
    {
        SetupErrorMessage = SelectedSource switch
        {
            OnboardingSourceKind.OnDevice =>
                SelectedModel is null ? null : _downloadErrors.Message(SelectedModel.Kind),

            OnboardingSourceKind.YourProvider => _providerErrorMessage,
            _ => null
        };

        RaiseGateChanged();
    }

}
