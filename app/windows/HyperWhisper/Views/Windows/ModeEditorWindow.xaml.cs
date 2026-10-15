using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using HyperWhisper.Data.Entities;
using HyperWhisper.Localization;
using HyperWhisper.Models;
using HyperWhisper.Services;
using HyperWhisper.Utilities;
using HyperWhisper.Views.Pages.Settings;

namespace HyperWhisper.Views.Windows;

public partial class ModeEditorWindow : Window
{
    private readonly Mode _mode;
    private readonly bool _isCreateMode;
    private bool _isLoading;
    private bool _isUpdatingCloudModels;

    public ModeEditorWindow(Mode mode)
    {
        InitializeComponent();
        // 700 DIP is taller than a 1080p work area at 175%; the body scrolls (issue #1500).
        WindowWorkAreaFit.Attach(this);
        _mode = mode;
        _isCreateMode = false;

        ApplyDefaultModeNameLock();
        Loaded += OnLoaded;
    }

    /// <summary>
    /// The default mode's name is fixed, and the field says so rather than just going
    /// grey (issue #494). Keyed on IsDefault - the mode's identity, not its label.
    ///
    /// Here and not in Loaded: it reads only _mode, which is fixed from construction,
    /// so a window that is never shown still applies the rule.
    /// </summary>
    private void ApplyDefaultModeNameLock()
    {
        ModeNameBox.IsEnabled = !_mode.IsDefault;
        ModeNameLockedHint.Visibility = _mode.IsDefault ? Visibility.Visible : Visibility.Collapsed;
        System.Windows.Automation.AutomationProperties.SetHelpText(
            ModeNameBox, _mode.IsDefault ? ModeNameLockedHint.Text : string.Empty);
    }

    public ModeEditorWindow(bool isCreateMode)
    {
        InitializeComponent();
        // 700 DIP is taller than a 1080p work area at 175%; the body scrolls (issue #1500).
        WindowWorkAreaFit.Attach(this);
        _isCreateMode = isCreateMode;

        // Create a new mode with sensible defaults
        var existingModes = ModeService.Instance.GetAllModes();
        _mode = new Mode
        {
            Id = Guid.NewGuid(),
            Name = "",
            Preset = "hyper",
            ProviderType = "local",
            Model = "local",
            ModelType = "base",
            CloudProvider = null,
            Language = "auto",
            IsDefault = false,
            IsSystemProvided = false,
            SortOrder = existingModes.Count,
            Punctuation = true,
            Capitalization = true,
            PostProcessingMode = 0,
            PostProcessingProvider = null,
            // Seed the spelling variant from the system region so a brand-new
            // mode opens on the user's own variant instead of always American.
            EnglishSpelling = EnglishSpellingRegionDefault.ForCurrentRegion(),
            CreatedDate = DateTime.UtcNow,
            ModifiedDate = DateTime.UtcNow
        };

        // No ApplyDefaultModeNameLock() here: this constructor sets IsDefault = false
        // above, so the lock could only ever be a no-op, and the XAML already ships
        // the unlocked state.
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Configure UI for ARM64 architecture before loading data
        ConfigureForArchitecture();

        LoadLocalModels();
        LoadLanguages();
        LoadPostProcessingProviders();
        // Note: Cloud models are loaded dynamically in LoadModeIntoEditor
        // based on the selected cloud provider
        LoadModeIntoEditor(_mode);

        // Configure UI for create vs edit mode
        if (_isCreateMode)
        {
            Title = Loc.S("mode.editor.title.create");
            DeleteModeButton.Visibility = Visibility.Collapsed;
            SaveModeButton.Content = Loc.S("modes.button.create");
        }

        // Update save button state based on name
        UpdateSaveButtonState();
    }

    /// <summary>
    /// Configures local provider availability for the current architecture.
    /// Local transcription remains visible when any native local engine is
    /// available, such as the ARM64 sherpa-onnx daemon.
    /// </summary>
    private void ConfigureForArchitecture()
    {
        if (!PlatformHelper.SupportsLocalTranscription)
        {
            // Show ARM64 info banner
            Arm64InfoBanner.Visibility = Visibility.Visible;

            // Hide the On-device source segment when no local engine can load.
            SourceOnDeviceSegment.Visibility = Visibility.Collapsed;

            // If the current mode uses an unavailable local engine, switch to cloud.
            if (!LocalModeModel.IsCloud(_mode) && !IsLocalEngineSupported(_mode.LocalEngine))
            {
                LocalModeModel.ApplyCloud(_mode);
                _mode.CloudProvider = "openai"; // Default to HyperWhisper Cloud (no API key needed)
            }
        }
        else if (!LocalModeModel.IsCloud(_mode) && !IsLocalEngineSupported(_mode.LocalEngine))
        {
            LocalModeModel.ApplyCloud(_mode);
            _mode.CloudProvider = "openai";
        }

        if (!PlatformHelper.SupportsLocalLlmPostProcessing)
        {
            // Hide the On-device post-processing segment and remove Local LLM from
            // the BYOK provider list.
            PpSourceOnDeviceSegment.Visibility = Visibility.Collapsed;

            var localLlmItem = PostProcessingProviderCombo.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(i => i.Tag?.ToString() == PostProcessingProvider.LocalLlm.ToStringValue());
            if (localLlmItem != null)
            {
                PostProcessingProviderCombo.Items.Remove(localLlmItem);
            }

            if (_mode.PostProcessingProvider == PostProcessingProvider.LocalLlm.ToStringValue())
            {
                _mode.PostProcessingProvider = null;
                _mode.PostProcessingMode = 0;
                _mode.LocalPostProcessingModel = null;
            }
        }

        // The "Your provider" transcription list is BYOK-only — HyperWhisper Cloud is
        // reached via its own source segment. Azure/Google were never in the list.
        var hwTranscriptionItem = CloudProviderCombo.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(i => i.Tag?.ToString() == "hyperwhisper");
        if (hwTranscriptionItem != null)
        {
            CloudProviderCombo.Items.Remove(hwTranscriptionItem);
        }

        // The BYOK post-processing list excludes HyperWhisper Cloud (its own segment)
        // and Local LLM (the On-device segment).
        var hwPpItem = PostProcessingProviderCombo.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(i => i.Tag?.ToString() == "hyperwhispercloud");
        if (hwPpItem != null)
        {
            PostProcessingProviderCombo.Items.Remove(hwPpItem);
        }
        var localLlmByokItem = PostProcessingProviderCombo.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(i => i.Tag?.ToString() == PostProcessingProvider.LocalLlm.ToStringValue());
        if (localLlmByokItem != null)
        {
            PostProcessingProviderCombo.Items.Remove(localLlmByokItem);
        }

        // Populate the HyperWhisper Cloud post-processing engine combo (Cerebras /
        // Anthropic / Grok). Anthropic is tagged "(Recommended)".

        // Build the HyperWhisper Cloud Provider dropdown from the shared catalog.

    }

    // =========================================================================
    // SOURCE SEGMENT HELPERS (3-way: On-device / HyperWhisper Cloud / Your provider)
    // =========================================================================

    /// <summary>
    /// The currently selected transcription source segment tag
    /// ("ondevice" / "hwcloud" / "yourprovider").
    /// </summary>
    private string SelectedTranscriptionSource() => SourceOnDeviceSegment.IsChecked == true ? "ondevice" : "yourprovider";

    /// <summary>
    /// Legacy provider-type string ("local" / "cloud") derived from the selected
    /// transcription source segment. Methods that branch on local-vs-cloud use this
    /// in place of the removed ProviderTypeCombo.
    /// </summary>
    private string SelectedProviderType()
        => SelectedTranscriptionSource() == "ondevice" ? "local" : "cloud";

    /// <summary>Selects the transcription source segment for the given source tag.</summary>
    private void SetTranscriptionSourceSegment(string source)
    {
        SourceOnDeviceSegment.IsChecked = source == "ondevice";
        SourceYourProviderSegment.IsChecked = source != "ondevice";
    }

    /// <summary>The currently selected post-processing source segment tag.</summary>
    private string SelectedPostProcessingSource() => PpSourceOnDeviceSegment.IsChecked == true ? "ondevice" : "yourprovider";

    /// <summary>Selects the post-processing source segment for the given source tag.</summary>
    private void SetPostProcessingSourceSegment(string source)
    {
        PpSourceOnDeviceSegment.IsChecked = source == "ondevice";
        PpSourceYourProviderSegment.IsChecked = source != "ondevice";
    }

    // =========================================================================
    // NAME VALIDATION
    // =========================================================================

    private void ModeNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading) return;
        UpdateSaveButtonState();
    }

    private bool HasValidDictationLanguage()
    {
        if (SelectedProviderType() != "cloud") return true;
        ResolveEffectiveCloudProviderAndModel(out var provider, out var modelId);
        if (provider != CloudTranscriptionProvider.AssemblyAI || modelId != "dictation") return true;

        var language = (LanguageCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var support = Services.SharedModelsCatalog.GetLanguageSupport("assemblyAI", CatalogKind.Voice, "dictation");
        return language != null && support.Codes.Contains(language);
    }

    private void UpdateSaveButtonState()
    {
        // Name is required
        if (string.IsNullOrWhiteSpace(ModeNameBox.Text) || !HasValidDictationLanguage())
        {
            SaveModeButton.IsEnabled = false;
            return;
        }

        // If local provider selected, check if the selected model is downloaded
        var providerType = SelectedProviderType();
        if (providerType == "local")
        {
            var tag = (LocalModelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            var (engine, modelId) = ParseModelTag(tag);

            if (engine == "parakeet")
            {
                var model = ParakeetModelInfo.AllModels.FirstOrDefault(m => m.Id == modelId);
                if (model != null)
                {
                    var modelService = new ParakeetModelService();
                    if (!modelService.IsModelDownloaded(model))
                    {
                        SaveModeButton.IsEnabled = false;
                        return;
                    }
                }
            }
            else
            {
                var model = WhisperModelInfo.AllModels.FirstOrDefault(m => m.Type == modelId);
                if (model != null)
                {
                    var modelService = new WhisperModelService();
                    if (!modelService.IsModelDownloaded(model))
                    {
                        SaveModeButton.IsEnabled = false;
                        return;
                    }
                }
            }
        }

        if (PostProcessingCheck.IsChecked == true &&
            IsLocalLlmPostProcessingSelected())
        {
            var modelId = (PostProcessingModelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            var localModel = LocalLlmModelInfo.GetById(modelId);
            if (localModel != null)
            {
                var modelService = new LocalLlmModelService();
                if (!modelService.IsModelDownloaded(localModel))
                {
                    SaveModeButton.IsEnabled = false;
                    return;
                }
            }
        }

        SaveModeButton.IsEnabled = true;
    }

    private bool IsLocalLlmPostProcessingSelected()
    {
        // Local LLM post-processing is now selected via the On-device source segment.
        return SelectedPostProcessingSource() == "ondevice";
    }

    // =========================================================================
    // TAG HELPERS
    // =========================================================================

    /// <summary>
    /// Parses a unified model combo tag like "whisper:base" or "parakeet:parakeet-v2"
    /// into (engine, modelId).
    /// </summary>
    private static (string engine, string modelId) ParseModelTag(string? tag)
    {
        if (string.IsNullOrEmpty(tag))
            return ("whisper", "base");

        var colonIndex = tag.IndexOf(':');
        if (colonIndex < 0)
            return ("whisper", tag);

        return (tag[..colonIndex], tag[(colonIndex + 1)..]);
    }

    // =========================================================================
    // DATA LOADING
    // =========================================================================

    private void LoadLocalModels()
    {
        LocalModelCombo.Items.Clear();

        // Whisper models (skip when the current architecture/runtime cannot load Whisper.net)
        if (PlatformHelper.SupportsWhisperTranscription)
        {
            var whisperService = new WhisperModelService();
            foreach (var model in WhisperModelInfo.AllModels)
            {
                var isDownloaded = whisperService.IsModelDownloaded(model);
                var item = new ComboBoxItem
                {
                    Content = $"{model.DisplayName} ({model.Size}){(isDownloaded ? "" : Loc.S("mode.editor.model.notDownloaded"))}",
                    Tag = $"whisper:{model.Type}"
                };
                LocalModelCombo.Items.Add(item);
            }
        }

        // Parakeet-family models (Parakeet, Qwen3 ASR, Nemotron) hosted by the sherpa-onnx daemon.
        if (PlatformHelper.SupportsParakeetTranscription)
        {
            var parakeetService = new ParakeetModelService();
            foreach (var model in ParakeetModelInfo.AllModels)
            {
                var isDownloaded = parakeetService.IsModelDownloaded(model);
                var langInfo = model.IsEnglishOnly ? "English" : "Multilingual";
                var item = new ComboBoxItem
                {
                    Content = $"{model.ProviderDisplayName} - {model.DisplayName} ({model.Size}, {langInfo}){(isDownloaded ? "" : Loc.S("mode.editor.parakeetModel.notDownloaded"))}",
                    Tag = $"parakeet:{model.Id}"
                };
                LocalModelCombo.Items.Add(item);
            }
        }
    }

    /// <summary>
    /// Loads cloud models for a specific provider into the CloudModelCombo dropdown.
    /// Called when the cloud provider selection changes.
    /// </summary>
    private void LoadCloudModels(CloudTranscriptionProvider provider, string? preferredModelId = null)
    {
        CloudModelCombo.Items.Clear();

        var allModels = CloudTranscriptionModels.GetModelsForProvider(provider);
        var canonicalPreferredModelId = !string.IsNullOrEmpty(preferredModelId)
            ? CloudTranscriptionModels.ResolveModelAlias(preferredModelId, provider)
            : preferredModelId;

        if (!string.IsNullOrEmpty(canonicalPreferredModelId) &&
            CloudModelShowAllCheck.IsChecked != true &&
            !CloudTranscriptionModels.GetPopularModelsForProvider(provider)
                .Any(model => model.Id.Equals(canonicalPreferredModelId, StringComparison.OrdinalIgnoreCase)))
        {
            SetCloudModelShowAll(true);
        }

        var showAll = CloudModelShowAllCheck.IsChecked == true;
        var selectableModels = (showAll ? allModels : CloudTranscriptionModels.GetPopularModelsForProvider(provider))
            .Where(m => m.IsAvailable)
            .ToList();

        // Retired/unavailable models (e.g. ElevenLabs scribe_v1) are excluded from the
        // "pick a new one" list above, but if the Mode's CURRENT selection is one of
        // them, keep it visible here rather than silently falling through to
        // SelectedIndex = 0 below and switching the user's mode to a different model
        // out from under them.
        if (!string.IsNullOrEmpty(canonicalPreferredModelId) &&
            !selectableModels.Any(m => m.Id.Equals(canonicalPreferredModelId, StringComparison.OrdinalIgnoreCase)))
        {
            var currentModel = allModels.FirstOrDefault(
                m => m.Id.Equals(canonicalPreferredModelId, StringComparison.OrdinalIgnoreCase));
            if (currentModel != null)
            {
                selectableModels.Add(currentModel);
            }
        }

        var models = selectableModels;
        foreach (var model in models)
        {
            var item = new ComboBoxItem
            {
                Content = showAll || !model.IsPopular
                    ? model.DisplayName
                    : Loc.S("mode.editor.cloudModel.popularLabel", model.DisplayName),
                Tag = model.Id
            };
            CloudModelCombo.Items.Add(item);
        }

        if (!string.IsNullOrEmpty(canonicalPreferredModelId))
        {
            foreach (ComboBoxItem item in CloudModelCombo.Items)
            {
                if (item.Tag?.ToString()?.Equals(canonicalPreferredModelId, StringComparison.OrdinalIgnoreCase) == true)
                {
                    CloudModelCombo.SelectedItem = item;
                    return;
                }
            }
        }

        if (CloudModelCombo.Items.Count > 0)
        {
            CloudModelCombo.SelectedIndex = 0;
        }
    }

    private void CloudModelShowAllCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _isUpdatingCloudModels) return;

        var providerTag = (CloudProviderCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var provider = CloudTranscriptionProviderExtensions.FromIdentifier(providerTag);
        var selectedModelId = (CloudModelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        LoadCloudModels(provider, selectedModelId);
    }

    private void SetCloudModelShowAll(bool showAll)
    {
        _isUpdatingCloudModels = true;
        try
        {
            CloudModelShowAllCheck.IsChecked = showAll;
        }
        finally
        {
            _isUpdatingCloudModels = false;
        }
    }

    /// <summary>
    /// Handles cloud model selection change to update description text.
    /// </summary>
    private void CloudModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CloudModelCombo.SelectedItem is ComboBoxItem selected)
        {
            var modelId = selected.Tag?.ToString();
            var providerTag = (CloudProviderCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            var provider = CloudTranscriptionProviderExtensions.FromIdentifier(providerTag);
            var model = CloudTranscriptionModels.GetById(modelId, provider);

            // Show description with pricing if available
            if (model != null)
            {
                var description = model.Description;
                if (model.PricePerMinute.HasValue)
                {
                    if (provider == CloudTranscriptionProvider.AssemblyAI
                        && model.Id.EndsWith("-medical", StringComparison.Ordinal))
                    {
                        description += $" - base ${model.PricePerMinute:F4}/min + separate Medical Mode add-on";
                    }
                    else
                    {
                        description += $" - ${model.PricePerMinute:F4}/min";
                    }
                }
                CloudModelDescText.Text = description;
            }
            else
            {
                CloudModelDescText.Text = "";
            }
        }
        else
        {
            CloudModelDescText.Text = "";
        }

        if (!_isLoading)
        {
            AutoSelectEnglishForModel();
            UpdateLanguagesForSelectedModel();
            UpdateAllWarnings();
        }
    }

    /// <summary>
    /// Handles cloud provider selection change.
    /// Updates the model dropdown, accuracy tier panel, and API key warning for the new provider.
    /// </summary>
    private void CloudProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;

        ApplySelectedCloudProviderPanels();
        UpdateLanguagesForSelectedModel();
        UpdateAllWarnings();
    }

    private static bool IsLocalEngineSupported(string? localEngine)
    {
        return string.Equals(localEngine, "parakeet", StringComparison.OrdinalIgnoreCase)
            ? PlatformHelper.SupportsParakeetTranscription
            : PlatformHelper.SupportsWhisperTranscription;
    }

    /// <summary>
    /// Resolves the effective routed STT provider + model id from the current
    /// cloud picker selection. HyperWhisper Cloud is a tier wrapper: the outer
    /// provider is "hyperwhisper", but the real upstream provider is the selected
    /// accuracy tier's X-STT-Provider value and the model is the selected tier
    /// model (CloudTierModelCombo). For every other (BYOK) provider the model is
    /// the CloudModelCombo selection. The tier's X-STT-Provider value is mapped
    /// through <see cref="CloudTranscriptionProviderExtensions.FromCatalogSttProvider"/>,
    /// which is the only function that knows the catalog-only spellings
    /// (<c>azure-mai</c>, <c>gemini-transcribe</c>); a tier the catalog does not
    /// name still resolves to <see cref="CloudTranscriptionProvider.None"/>, so
    /// callers fall through to their default (full language list) branch.
    /// <paramref name="modelId"/> is
    /// never null (empty string when unselected, matching the X-STT-Model
    /// "provider default" convention).
    /// </summary>
    private void ResolveEffectiveCloudProviderAndModel(out CloudTranscriptionProvider provider, out string modelId)
    {
        // HyperWhisper Cloud is now its own source segment (not a CloudProviderCombo
        // entry): resolve the upstream provider + model from the accuracy tier and
        // tier model combo. Otherwise it's a BYOK provider from CloudProviderCombo.

        var providerTag = (CloudProviderCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        provider = CloudTranscriptionProviderExtensions.FromIdentifier(providerTag);
        modelId = (CloudModelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Shows the BYOK ("Your provider") transcription panels for the provider selected
    /// in <see cref="CloudProviderCombo"/> (HyperWhisper Cloud is excluded from that
    /// list — it has its own source segment).
    /// </summary>
    private void ApplySelectedCloudProviderPanels()
    {

        if (CloudProviderCombo.SelectedItem is ComboBoxItem selected)
        {
            var providerTag = selected.Tag?.ToString();
            var provider = CloudTranscriptionProviderExtensions.FromIdentifier(providerTag);

            // Every provider shows the model row, including Grok, whose single
            // implicit model is listed as the one entry.
            CloudModelPanel.Visibility = Visibility.Visible;
            SetCloudModelShowAll(false);
            LoadCloudModels(provider);

            // Show Gemini custom prompt panel only for Gemini provider
            GeminiCustomPromptPanel.Visibility = provider == CloudTranscriptionProvider.Gemini
                ? Visibility.Visible : Visibility.Collapsed;

            UpdateApiKeyWarning("cloud", provider);
        }
    }

    // =========================================================================
    // HYPERWHISPER CLOUD POST-PROCESSING ENGINE + MODEL
    // =========================================================================

    private void LoadLanguages()
    {
        LanguageCombo.Items.Clear();

        foreach (var lang in LanguageInfo.AllLanguages)
        {
            var item = new ComboBoxItem
            {
                Content = lang.DisplayName,
                Tag = lang.Code
            };
            LanguageCombo.Items.Add(item);
        }
    }

    // =========================================================================
    // EVENT HANDLERS
    // =========================================================================

    private void SourceSegment_Checked(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;

        var source = SelectedTranscriptionSource();
        // UpdateSourcePanels sets the API-key warning with the correct provider for
        // each source (HW Cloud → no key; BYOK → the combo provider; local → cleared).
        UpdateSourcePanels(source);
        // The Language row's visibility follows the SOURCE as well as the local
        // model: leaving an English-only local model (Parakeet v2, Whisper *.en)
        // for a cloud source must bring the picker back, and returning to one must
        // hide it and force English again (#1641). Runs before the list filter, in
        // the same order as LocalModelCombo_SelectionChanged, so the filter sees
        // the forced "en" and keeps it selected.
        AutoSelectEnglishForModel();
        UpdateLanguagesForSelectedModel();
        UpdateAllWarnings();
        UpdateSaveButtonState();
    }

    /// <summary>
    /// Shows the transcription model controls for the selected source segment:
    /// On-device → local model picker; HyperWhisper Cloud → Engine (tier) + Model;
    /// Your provider → BYOK provider + model. When switching INTO a cloud source,
    /// the CloudProvider context is set so downstream resolvers behave correctly.
    /// </summary>
    private void UpdateSourcePanels(string source)
    {
        switch (source)
        {
            case "ondevice":
                LocalModelPanel.Visibility = Visibility.Visible;
                CloudProviderPanel.Visibility = Visibility.Collapsed;
                CloudModelPanel.Visibility = Visibility.Collapsed;

                GeminiCustomPromptPanel.Visibility = Visibility.Collapsed;
                UpdateApiKeyWarning("local");
                break;

            default: // "yourprovider"
                LocalModelPanel.Visibility = Visibility.Collapsed;
                ParakeetLanguageWarning.Visibility = Visibility.Collapsed;
                CloudProviderPanel.Visibility = Visibility.Visible;
                // Ensure a BYOK provider is selected (HyperWhisper was removed).
                if (CloudProviderCombo.SelectedIndex == -1 && CloudProviderCombo.Items.Count > 0)
                    CloudProviderCombo.SelectedIndex = 0;
                ApplySelectedCloudProviderPanels();
                break;
        }
    }

    private void UpdateLocalModelStatus()
    {
        if (LocalModelCombo.SelectedItem is not ComboBoxItem selected) return;

        var (engine, modelId) = ParseModelTag(selected.Tag?.ToString());

        if (engine == "parakeet")
        {
            var model = ParakeetModelInfo.AllModels.FirstOrDefault(m => m.Id == modelId);
            if (model != null)
            {
                var modelService = new ParakeetModelService();
                var isDownloaded = modelService.IsModelDownloaded(model);
                LocalModelStatusText.Text = isDownloaded
                    ? ReadyTextForParakeetFamily(model)
                    : "Model not downloaded. Go to Model Library to download.";
            }
        }
        else
        {
            var model = WhisperModelInfo.AllModels.FirstOrDefault(m => m.Type == modelId);
            if (model != null)
            {
                var modelService = new WhisperModelService();
                var isDownloaded = modelService.IsModelDownloaded(model);
                LocalModelStatusText.Text = isDownloaded
                    ? $"Ready - {model.RecommendedVramDisplay} VRAM"
                    : "Model not downloaded. Go to Home to download.";
            }
        }
    }

    private void LocalModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;

        UpdateLocalModelStatus();
        AutoSelectEnglishForModel();
        UpdateLanguagesForSelectedModel();
        UpdateAllWarnings();
        UpdateSaveButtonState();
    }

    private void UpdateParakeetLanguageWarning()
    {
        var tag = (LocalModelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var (engine, modelId) = ParseModelTag(tag);

        if (engine != "parakeet")
        {
            ParakeetLanguageWarning.Visibility = Visibility.Collapsed;
            return;
        }

        var model = ParakeetModelInfo.AllModels.FirstOrDefault(m => m.Id == modelId);
        var language = (LanguageCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();

        if (model == null)
        {
            ParakeetLanguageWarning.Visibility = Visibility.Collapsed;
            return;
        }

        if (model.IsEnglishOnly && language != "en")
        {
            ParakeetLanguageWarningText.Text = $"{model.DisplayName} supports English only. Language will be set to English.";
            ParakeetLanguageWarning.Visibility = Visibility.Visible;
        }
        else if (!model.IsEnglishOnly && !string.IsNullOrEmpty(language) && language != "auto" && !model.IsLanguageSupported(language))
        {
            ParakeetLanguageWarningText.Text = $"{model.DisplayName} does not support this language. Choose another local model or a cloud provider.";
            ParakeetLanguageWarning.Visibility = Visibility.Visible;
        }
        else
        {
            ParakeetLanguageWarning.Visibility = Visibility.Collapsed;
        }
    }

    private static string ReadyTextForParakeetFamily(ParakeetModelInfo model)
    {
        return model.Engine switch
        {
            ParakeetEngine.Qwen3 => "Ready - CPU inference",
            ParakeetEngine.NemotronMl => "Ready - CPU streaming",
            _ => PlatformHelper.IsArm64
                ? "Ready - CPU inference"
                : "Ready - DirectML acceleration with CPU fallback"
        };
    }

    // =========================================================================
    // PRESET HANDLING
    // =========================================================================

    private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;

        if (PresetCombo.SelectedItem is ComboBoxItem selected)
        {
            var presetTag = selected.Tag?.ToString();
            PresetDescText.Text = PresetTypeExtensions.FromString(presetTag ?? "hyper").ToDescription();

            // Show/hide custom instructions panel
            CustomInstructionsPanel.Visibility = presetTag == "custom" ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // =========================================================================
    // LANGUAGE HANDLING
    // =========================================================================

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        UpdateEnglishSpellingVisibility();
        UpdateNova3Warning();
        UpdateSaveButtonState();
    }

    /// <summary>
    /// Selects the English spelling item for a mode. A mode that has no stored
    /// value falls back to the system region's variant, so a new mode opens on
    /// the user's own spelling instead of always American.
    /// </summary>
    private void SelectEnglishSpelling(string? storedValue)
    {
        if (EnglishSpellingCombo.Items.Count == 0)
        {
            return;
        }

        var value = string.IsNullOrWhiteSpace(storedValue)
            ? EnglishSpellingRegionDefault.ForCurrentRegion()
            : storedValue;

        if (!TrySelectEnglishSpellingItem(value))
        {
            // Unrecognised stored value — fall back to the region default, then
            // to the first item so the combo is never left unselected.
            if (!TrySelectEnglishSpellingItem(EnglishSpellingRegionDefault.ForCurrentRegion()))
            {
                EnglishSpellingCombo.SelectedIndex = 0;
            }
        }
    }

    private bool TrySelectEnglishSpellingItem(string value)
    {
        foreach (ComboBoxItem item in EnglishSpellingCombo.Items)
        {
            if (item.Tag?.ToString() == value)
            {
                EnglishSpellingCombo.SelectedItem = item;
                return true;
            }
        }
        return false;
    }

    private void UpdateEnglishSpellingVisibility()
    {
        if (LanguageCombo.SelectedItem is ComboBoxItem selected)
        {
            var lang = selected.Tag?.ToString();
            // Show English spelling only for English or Auto-detect
            var isEnglishRelated = lang == "en" || lang == "auto" || string.IsNullOrEmpty(lang);
            var isPostProcessingEnabled = PostProcessingCheck.IsChecked == true;

            EnglishSpellingPanel.Visibility = (isEnglishRelated && isPostProcessingEnabled)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    // =========================================================================
    // PUNCTUATION / REMOVE TRAILING PERIOD HANDLING
    // =========================================================================

    private void PunctuationCheck_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;

        bool punctuationEnabled = PunctuationCheck.IsChecked == true;

        // Only toggle RemoveTrailingPeriod visibility when post-processing is enabled
        // (when post-processing is off, RemoveTrailingPeriod is always visible standalone)
        if (PostProcessingCheck.IsChecked == true)
        {
            RemoveTrailingPeriodCheck.Visibility = punctuationEnabled ? Visibility.Visible : Visibility.Collapsed;
        }

        if (!punctuationEnabled)
        {
            RemoveTrailingPeriodCheck.IsChecked = false;
        }
    }

    // =========================================================================
    // USER SYSTEM PROMPT HANDLING
    // =========================================================================

    private void UserPromptCheck_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;

        UserPromptPanel.Visibility = UserPromptCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        if (UserPromptCheck.IsChecked != true)
        {
            UserPromptBox.Text = "";
        }
    }

    private void ClearPromptButton_Click(object sender, RoutedEventArgs e)
    {
        UserPromptBox.Text = "";
    }

    private void UserPromptBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UserPromptCharCount.Text = $"{UserPromptBox.Text.Length}/8000";
    }

    private void GeminiCustomPromptBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        GeminiCustomPromptCharCount.Text = $"{GeminiCustomPromptBox.Text.Length}/2000";
        GeminiCustomPromptPlaceholder.Visibility = string.IsNullOrEmpty(GeminiCustomPromptBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Updates the API key warning based on provider type and selected cloud provider.
    /// Shows a warning if the required API key is not configured.
    /// </summary>
    private void UpdateApiKeyWarning(string providerType, CloudTranscriptionProvider? cloudProvider = null)
    {
        if (providerType != "cloud")
        {
            ApiKeyWarning.Visibility = Visibility.Collapsed;
            return;
        }

        // Get selected cloud provider if not passed
        if (cloudProvider == null)
        {
            var selectedItem = CloudProviderCombo.SelectedItem as ComboBoxItem;
            var providerTag = selectedItem?.Tag?.ToString();
            cloudProvider = CloudTranscriptionProviderExtensions.FromIdentifier(providerTag);
        }

        // HyperWhisper Cloud and the HW-Cloud-routed providers (Azure MAI,
        // Google Chirp) don't require an API key.
        if (cloudProvider.HasValue && !cloudProvider.Value.RequiresApiKey())
        {
            ApiKeyWarning.Visibility = Visibility.Collapsed;
            return;
        }

        // Check if API key is configured for this provider
        bool hasKey = cloudProvider switch
        {
            // These share API keys with post-processing providers
            CloudTranscriptionProvider.OpenAI => ApiKeyService.Instance.HasApiKey(PostProcessingProvider.OpenAI),
            CloudTranscriptionProvider.Groq => ApiKeyService.Instance.HasApiKey(PostProcessingProvider.Groq),
            CloudTranscriptionProvider.Gemini => ApiKeyService.Instance.HasApiKey(PostProcessingProvider.Gemini),
            CloudTranscriptionProvider.Grok => ApiKeyService.Instance.HasApiKey(PostProcessingProvider.Grok),

            // These have their own transcription API keys
            CloudTranscriptionProvider.Deepgram => ApiKeyService.Instance.HasApiKey(TranscriptionApiKeyType.Deepgram),
            CloudTranscriptionProvider.AssemblyAI => ApiKeyService.Instance.HasApiKey(TranscriptionApiKeyType.AssemblyAI),
            CloudTranscriptionProvider.ElevenLabs => ApiKeyService.Instance.HasApiKey(TranscriptionApiKeyType.ElevenLabs),
            CloudTranscriptionProvider.Mistral => ApiKeyService.Instance.HasApiKey(TranscriptionApiKeyType.Mistral),
            CloudTranscriptionProvider.Soniox => ApiKeyService.Instance.HasApiKey(TranscriptionApiKeyType.Soniox),
            CloudTranscriptionProvider.GeminiTranscribe => ApiKeyService.Instance.HasApiKey(TranscriptionApiKeyType.GeminiTranscribe),
            CloudTranscriptionProvider.Meta => ApiKeyService.Instance.HasApiKey(TranscriptionApiKeyType.Meta),

            _ => false
        };

        if (!hasKey)
        {
            var providerName = cloudProvider?.GetDisplayName() ?? "Cloud";
            ApiKeyWarning.Visibility = Visibility.Visible;
            ApiKeyWarningText.Text = $"{providerName} API key not configured. Add it in the Model Library API keys manager before using this provider.";
        }
        else
        {
            ApiKeyWarning.Visibility = Visibility.Collapsed;
        }
    }

    private void PostProcessingCheck_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;

        var ppEnabled = PostProcessingCheck.IsChecked == true;
        PostProcessingSettingsPanel.Visibility = ppEnabled ? Visibility.Visible : Visibility.Collapsed;

        if (ppEnabled)
        {
            if (PresetCombo.SelectedIndex == -1 && PresetCombo.Items.Count > 0)
                PresetCombo.SelectedIndex = 0;
            // Show the controls for the currently selected post-processing source.
            ApplyPostProcessingSourcePanels();
        }

        // Punctuation & capitalization are LLM instructions — hide when post-processing is off
        PunctuationCheck.Visibility = ppEnabled ? Visibility.Visible : Visibility.Collapsed;
        CapitalizationCheck.Visibility = ppEnabled ? Visibility.Visible : Visibility.Collapsed;

        if (!ppEnabled)
        {
            // Show remove trailing period standalone when post-processing is off
            RemoveTrailingPeriodCheck.Visibility = Visibility.Visible;
            PunctuationCheck.IsChecked = false;
            CapitalizationCheck.IsChecked = false;
            ScreenOCRCheck.IsChecked = false;
        }
        else
        {
            // When post-processing is on, remove trailing period follows punctuation toggle
            RemoveTrailingPeriodCheck.Visibility = PunctuationCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        // Update English spelling visibility when post-processing toggle changes
        UpdateEnglishSpellingVisibility();
        UpdateSaveButtonState();
    }

    /// <summary>
    /// Drives the post-processing model controls from the selected source segment:
    /// On-device → Local LLM model picker; HyperWhisper Cloud → Engine + Model;
    /// Your provider → BYOK provider + model. The PostProcessingProvider context is
    /// set here so the save path and warnings resolve correctly.
    /// </summary>
    private void PpSourceSegment_Checked(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        ApplyPostProcessingSourcePanels();
        UpdateSaveButtonState();
    }

    private void ApplyPostProcessingSourcePanels()
    {
        var source = SelectedPostProcessingSource();
        switch (source)
        {
            case "ondevice":
                // Local LLM: hide BYOK provider + HW-Cloud engine; show the model
                // picker populated with on-device GGUF models.
                PostProcessingProviderPanel.Visibility = Visibility.Collapsed;

                PostProcessingModelPanel.Visibility = Visibility.Visible;
                LoadPostProcessingModels(PostProcessingProvider.LocalLlm);
                break;

            default: // "yourprovider"

                PostProcessingProviderPanel.Visibility = Visibility.Visible;
                if (PostProcessingProviderCombo.SelectedIndex == -1 && PostProcessingProviderCombo.Items.Count > 0)
                    PostProcessingProviderCombo.SelectedIndex = 0;
                ApplySelectedPostProcessingProviderPanels();
                break;
        }
    }

    private void PostProcessingProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        // Only relevant for the "Your provider" source — the BYOK list excludes
        // HyperWhisper Cloud and Local LLM (both reached via source segments).
        if (SelectedPostProcessingSource() == "yourprovider")
        {
            ApplySelectedPostProcessingProviderPanels();
        }
        UpdateSaveButtonState();
    }

    /// <summary>
    /// Shows/populates the BYOK post-processing model picker for the provider selected
    /// in <see cref="PostProcessingProviderCombo"/>. Custom endpoints carry their model
    /// in the endpoint config, so the picker is hidden for them.
    /// </summary>
    private void ApplySelectedPostProcessingProviderPanels()
    {
        if (PostProcessingProviderCombo.SelectedItem is not ComboBoxItem selected) return;

        var providerStr = selected.Tag?.ToString();

        // Hide model selection for custom endpoints (model is in endpoint config)
        if (CustomPostProcessingEndpoint.IsCustomProviderString(providerStr))
        {
            PostProcessingModelPanel.Visibility = Visibility.Collapsed;
            LocalLlmOpenModelsSettingsButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            PostProcessingModelPanel.Visibility = Visibility.Visible;
            var provider = PostProcessingProviderExtensions.FromString(providerStr ?? "");
            LoadPostProcessingModels(provider);
        }
    }

    /// <summary>
    /// Populates the post-processing provider ComboBox dynamically,
    /// including custom endpoints after a separator.
    /// </summary>
    private void LoadPostProcessingProviders()
    {
        _isLoading = true;

        // Keep the static XAML items (they are already in the ComboBox)
        // Just append custom endpoints after them

        // Remove any previously added custom endpoint items (separator + items)
        var itemsToRemove = new List<object>();
        bool foundSeparator = false;
        foreach (var item in PostProcessingProviderCombo.Items)
        {
            if (item is Separator)
            {
                foundSeparator = true;
                itemsToRemove.Add(item);
            }
            else if (foundSeparator)
            {
                itemsToRemove.Add(item);
            }
        }
        foreach (var item in itemsToRemove)
        {
            PostProcessingProviderCombo.Items.Remove(item);
        }

        // Add custom endpoints if any exist
        var customEndpoints = CustomEndpointManager.Instance.GetAllEndpoints();
        if (customEndpoints.Count > 0)
        {
            PostProcessingProviderCombo.Items.Add(new Separator());
            foreach (var endpoint in customEndpoints)
            {
                PostProcessingProviderCombo.Items.Add(new ComboBoxItem
                {
                    Content = endpoint.Name,
                    Tag = endpoint.ProviderString
                });
            }
        }

        _isLoading = false;
    }

    private void LoadPostProcessingModels(PostProcessingProvider provider)
    {
        PostProcessingModelCombo.Items.Clear();

        var models = LanguageModelInfo.GetModelsForProvider(provider);
        foreach (var model in models)
        {
            var content = model.DisplayName;
            if (provider == PostProcessingProvider.LocalLlm)
            {
                var localModel = LocalLlmModelInfo.GetById(model.Id);
                if (localModel != null)
                {
                    var modelService = new LocalLlmModelService();
                    var isDownloaded = modelService.IsModelDownloaded(localModel);
                    // Plan-aware: drops "(Recommended)" on machines whose local-LLM plan is CPU-only.
                    content = $"{LocalLlmGpuHelper.DisplayNameFor(localModel)} ({localModel.Size}){(isDownloaded ? "" : Loc.S("settings.models.localLlm.notDownloadedSuffix"))}";
                }
            }

            var item = new ComboBoxItem
            {
                Content = content,
                Tag = model.Id
            };
            PostProcessingModelCombo.Items.Add(item);
        }

        if (PostProcessingModelCombo.Items.Count > 0)
        {
            PostProcessingModelCombo.SelectedIndex = 0;
        }
    }

    private void PostProcessingModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PostProcessingModelCombo.SelectedItem is ComboBoxItem selected)
        {
            var modelId = selected.Tag?.ToString();
            var model = LanguageModelInfo.GetById(modelId);
            // Local LLM is selected via the On-device source segment, not a provider-combo
            // item (ConfigureForArchitecture removes "local_llm" from the combo). Detect it
            // the same way as IsLocalLlmPostProcessingSelected() so the download-required
            // description + Open Models Settings button render for an undownloaded GGUF model.
            if (IsLocalLlmPostProcessingSelected())
            {
                var localModel = LocalLlmModelInfo.GetById(modelId);
                if (localModel != null)
                {
                    var modelService = new LocalLlmModelService();
                    var isDownloaded = modelService.IsModelDownloaded(localModel);
                    PostProcessingModelDescText.Text = isDownloaded
                        ? Loc.S("mode.editor.localLlm.readyDescription", localModel.Description)
                        : Loc.S("mode.editor.localLlm.downloadDescription", localModel.Description);
                    LocalLlmOpenModelsSettingsButton.Visibility = isDownloaded ? Visibility.Collapsed : Visibility.Visible;
                    UpdateSaveButtonState();
                    return;
                }
            }

            PostProcessingModelDescText.Text = model?.Description ?? "";
            LocalLlmOpenModelsSettingsButton.Visibility = Visibility.Collapsed;
            UpdateSaveButtonState();
        }
        else
        {
            PostProcessingModelDescText.Text = "";
            LocalLlmOpenModelsSettingsButton.Visibility = Visibility.Collapsed;
            UpdateSaveButtonState();
        }
    }

    private void LocalLlmOpenModelsSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedModelId = (PostProcessingModelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var settingsWindow = new Window
        {
            Title = Loc.S("settings.section.models"),
            Width = 720,
            Height = 760,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ModelsSettingsPage()
        };

        // 760 DIP is taller than a 1080p work area at 175% (issue #1500).
        WindowWorkAreaFit.Attach(settingsWindow);
        settingsWindow.ShowDialog();

        LoadPostProcessingModels(PostProcessingProvider.LocalLlm);
        if (!string.IsNullOrEmpty(selectedModelId))
        {
            foreach (ComboBoxItem item in PostProcessingModelCombo.Items)
            {
                if (item.Tag?.ToString() == selectedModelId)
                {
                    PostProcessingModelCombo.SelectedItem = item;
                    break;
                }
            }
        }
        UpdateSaveButtonState();
    }

    private void SaveModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!HasValidDictationLanguage())
        {
            LanguageCombo.Focus();
            return;
        }

        _mode.Name = ModeNameBox.Text.Trim();

        // Save preset
        if (PresetCombo.SelectedItem is ComboBoxItem presetItem)
        {
            _mode.Preset = presetItem.Tag?.ToString() ?? "hyper";
        }
        else if (string.IsNullOrEmpty(_mode.Preset) || _mode.Preset == "voiceToText")
        {
            _mode.Preset = "hyper";
        }

        // Save custom instructions (only relevant for Custom preset, but always save)
        _mode.CustomInstructions = CustomInstructionsBox.Text.Trim();

        var transcriptionSource = SelectedTranscriptionSource();
        _mode.ProviderType = transcriptionSource == "ondevice" ? "local" : "cloud";

        if (_mode.ProviderType == "cloud")
        {
            // The On-device arm below writes a local id into Model; put the cloud
            // sentinel back so macOS does not restore this mode as local (#1477).
            LocalModeModel.ApplyCloud(_mode);

            // HyperWhisper Cloud is its own source segment; "Your provider" is BYOK.
            if (CloudProviderCombo.SelectedItem is ComboBoxItem providerComboItem)
            {
                _mode.CloudProvider = providerComboItem.Tag?.ToString() ?? "openai";
            }

            if (CloudModelCombo.SelectedItem is ComboBoxItem cloudItem)
            {
                _mode.CloudTranscriptionModel = cloudItem.Tag?.ToString() ?? "whisper-1";
                _mode.CloudTranscriptionDomain = null;
            }
            else
            {
                _mode.CloudTranscriptionDomain = null;
            }

            // Save the cloud accuracy tier (for HyperWhisper Cloud). The Provider
            // row is a company, so the tier comes from the selected model. The
            // Provider combo always has rows (LoadCloudTierProviders falls back to
            // the CloudAccuracyTier cases when the catalog is empty), so this
            // resolves for any selection; the guard only skips the "nothing
            // selected at all" case, where keeping the stored tier beats
            // overwriting it with FromString's Deepgram default.

            // Save Gemini custom prompt
            var geminiPromptText = GeminiCustomPromptBox.Text?.Trim() ?? "";
            _mode.GeminiCustomPrompt = string.IsNullOrEmpty(geminiPromptText) ? null : geminiPromptText;
        }
        else
        {
            var tag = (LocalModelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            var (engine, modelId) = ParseModelTag(tag);
            _mode.LocalEngine = engine;

            if (engine == "parakeet")
            {
                _mode.LocalParakeetModel = modelId;
            }
            else
            {
                _mode.ModelType = modelId;
            }

            // A new mode is seeded Model = "cloud" / CloudProvider = "hyperwhisper"
            // (constructor above). Rewrite both so the row stops saying "cloud":
            // macOS and Linux route on those two fields when a backup crosses (#1477).
            LocalModeModel.ApplyOnDevice(_mode);
        }

        if (LanguageCombo.SelectedItem is ComboBoxItem langItem)
        {
            _mode.Language = langItem.Tag?.ToString() ?? "auto";
        }

        // Save text processing toggles (always save, regardless of post-processing state)
        _mode.Punctuation = PunctuationCheck.IsChecked == true;
        _mode.Capitalization = CapitalizationCheck.IsChecked == true;
        _mode.ProfanityFilter = ProfanityFilterCheck.IsChecked == true;
        _mode.EnableScreenOCR = ScreenOCRCheck.IsChecked == true;
        _mode.RemoveTrailingPeriod = RemoveTrailingPeriodCheck.IsChecked == true;

        // Save English spelling
        if (EnglishSpellingCombo.SelectedItem is ComboBoxItem spellingItem)
        {
            _mode.EnglishSpelling = spellingItem.Tag?.ToString() ?? "american";
        }

        // Save user system prompt
        _mode.UserSystemPrompt = UserPromptCheck.IsChecked == true ? UserPromptBox.Text.Trim() : "";

        // Always persist the HyperWhisper Cloud post-processing engine/model choice
        // so it survives switching the PP source segment away and back.

        if (PostProcessingCheck.IsChecked == true)
        {
            // The post-processing provider is driven by the source segment, not a
            // visible "HyperWhisper Cloud" combo item: On-device → Local LLM,
            // HyperWhisper Cloud → hyperwhispercloud, Your provider → BYOK combo.
            var ppSource = SelectedPostProcessingSource();
            if (ppSource == "ondevice")
            {
                _mode.PostProcessingProvider = PostProcessingProvider.LocalLlm.ToStringValue();
            }
            else if (PostProcessingProviderCombo.SelectedItem is ComboBoxItem ppProviderItem)
            {
                _mode.PostProcessingProvider = ppProviderItem.Tag?.ToString();
            }

            _mode.PostProcessingMode = _mode.PostProcessingProvider == PostProcessingProvider.LocalLlm.ToStringValue()
                ? 2
                : 1;

            // Save language model (only meaningful for On-device + BYOK; HyperWhisper
            // Cloud uses CloudPostProcessingModel above). Keep the existing value when
            // the HW-Cloud source is active so switching providers preserves it.
            if (ppSource != "hwcloud" && PostProcessingModelCombo.SelectedItem is ComboBoxItem ppModelItem)
            {
                if (ppSource == "ondevice")
                {
                    _mode.LocalPostProcessingModel = ppModelItem.Tag?.ToString();
                }
                else
                {
                    _mode.LanguageModel = ppModelItem.Tag?.ToString();
                }
            }
        }
        else
        {
            _mode.PostProcessingMode = 0;
            _mode.EnableScreenOCR = false;
        }

        _mode.ModifiedDate = DateTime.UtcNow;

        if (_isCreateMode)
        {
            ModeService.Instance.SaveMode(_mode);
        }
        else
        {
            ModeService.Instance.UpdateMode(_mode);
        }

        DialogResult = true;
        Close();
    }

    private void DeleteModeButton_Click(object sender, RoutedEventArgs e)
    {
        var result = WpfMessageBox.Show(
            Loc.S("mode.editor.delete.confirm.message", _mode.Name),
            Loc.S("mode.editor.delete.confirm.title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        var deleted = ModeService.Instance.DeleteMode(_mode.Id);

        if (!deleted)
        {
            WpfMessageBox.Show(
                Loc.S("mode.editor.delete.cannotDelete.message"),
                Loc.S("mode.editor.delete.cannotDelete.title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        DialogResult = true; // Treating delete as a successful "change" that requires parent refresh
        Close();
    }

    // =========================================================================
    // VOCABULARY & MODEL WARNINGS
    // =========================================================================

    /// <summary>
    /// Updates vocabulary warning visibility based on selected provider/model.
    /// Shows warning for providers that don't support custom vocabulary.
    /// </summary>
    private void UpdateVocabularyWarning()
    {
        bool showWarning = false;
        string warningText = Loc.S("mode.editor.warning.vocabularyUnsupported");

        var providerType = SelectedProviderType();

        if (providerType == "local")
        {
            var tag = (LocalModelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            var (engine, _) = ParseModelTag(tag);
            if (engine == "parakeet")
            {
                showWarning = true;
                warningText = Loc.S("mode.editor.warning.vocabularyUnsupported.parakeet");
            }
        }
        else if (providerType == "cloud")
        {
            // HyperWhisper Cloud is detected from the SOURCE segment, not the BYOK
            // provider combo — that combo no longer carries a "hyperwhisper" entry
            // (HW Cloud has its own source segment). When the HW Cloud source is
            // active the BYOK combo is irrelevant, so don't read it.
            var cloudProvider = (CloudProviderCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            var cloudModel = (CloudModelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();

            // HW Cloud accuracy tiers: catalog flags whether the chosen tier +
            // model supports custom vocabulary (ElevenLabs scribe_v1 doesn't
            // while scribe_v2 does). Prefer the
            // per-model flag so the warning matches the send-path gate. If the
            // tier string is null/empty (initial render, catalog load failure)
            // suppress the warning rather than guess.

            if (cloudProvider == "elevenlabs" && cloudModel == "scribe_v1")
            {
                showWarning = true;
                warningText = Loc.S("mode.editor.warning.vocabularyUnsupported.elevenlabs");
            }
            // Deepgram base and whisper models don't support vocabulary
            else if (cloudProvider == "deepgram")
            {
                if (cloudModel?.StartsWith("base") == true)
                {
                    showWarning = true;
                    warningText = Loc.S("mode.editor.warning.vocabularyUnsupported.deepgramBase");
                }
                else if (cloudModel?.StartsWith("whisper") == true)
                {
                    showWarning = true;
                    warningText = Loc.S("mode.editor.warning.vocabularyUnsupported.deepgramWhisper");
                }
            }
        }

        VocabularyWarning.Visibility = showWarning ? Visibility.Visible : Visibility.Collapsed;
        VocabularyWarningText.Text = warningText;
    }

    /// <summary>
    /// Updates Nova-3 auto-detect warning visibility.
    /// Shows warning when using Deepgram Nova models with auto-detect language.
    /// </summary>
    private void UpdateNova3Warning()
    {
        var providerType = SelectedProviderType();
        // HyperWhisper Cloud is detected from the SOURCE segment, not the BYOK
        // provider combo — that combo no longer carries a "hyperwhisper" entry,
        // so the Nova-3 tier check below would never fire if read from it.
        var cloudProvider = (CloudProviderCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var cloudModel = (CloudModelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var language = (LanguageCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();

        bool show = providerType == "cloud"
            && ((cloudProvider == "deepgram" && cloudModel?.Contains("nova") == true)
               )
            && (language == "auto" || string.IsNullOrEmpty(language));

        Nova3Warning.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Updates all provider/model warnings.
    /// </summary>
    private void UpdateAllWarnings()
    {
        UpdateVocabularyWarning();
        UpdateParakeetLanguageWarning();
        UpdateNova3Warning();
    }

    /// <summary>
    /// Auto-selects English language for English-only local models and hides the
    /// language row; shows and enables it again for every other model and source.
    /// Every handler that can change the source or the local model must call this
    /// (#1641: the source segment did not, so the row stayed hidden on Cloud).
    /// </summary>
    private void AutoSelectEnglishForModel()
    {
        var providerType = SelectedProviderType();
        bool isEnglishOnly = false;

        if (providerType == "local")
        {
            var tag = (LocalModelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            var (engine, modelId) = ParseModelTag(tag);

            if (engine == "parakeet")
            {
                var model = ParakeetModelInfo.AllModels.FirstOrDefault(m => m.Id == modelId);
                if (model?.IsEnglishOnly == true)
                    isEnglishOnly = true;
            }
            else if (modelId?.EndsWith(".en") == true)
            {
                isEnglishOnly = true;
            }
        }

        if (isEnglishOnly)
        {
            SelectLanguage("en");
            LanguageCombo.IsEnabled = false;
            LanguagePanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            LanguageCombo.IsEnabled = true;
            LanguagePanel.Visibility = Visibility.Visible;
        }
    }

    private void ReplaceLanguageItems(IEnumerable<LanguageInfo> languages)
    {
        var currentLang = (LanguageCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();

        LanguageCombo.Items.Clear();
        foreach (var lang in languages)
        {
            LanguageCombo.Items.Add(new ComboBoxItem { Content = lang.DisplayName, Tag = lang.Code });
        }

        bool found = false;
        if (!string.IsNullOrEmpty(currentLang))
        {
            foreach (ComboBoxItem item in LanguageCombo.Items)
            {
                if (item.Tag?.ToString() == currentLang) { LanguageCombo.SelectedItem = item; found = true; break; }
            }
        }
        if (!found) SelectLanguage("auto");
    }

    /// <summary>
    /// Filters the language dropdown based on the selected model.
    /// Parakeet v3 shows only its supported languages + Automatic.
    /// Whisper / Cloud restores the full 126-language list.
    /// English-only models are handled separately by AutoSelectEnglishForModel.
    /// </summary>
    private void UpdateLanguagesForSelectedModel()
    {
        var providerType = SelectedProviderType();

        if (providerType == "local")
        {
            var tag = (LocalModelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            var (engine, modelId) = ParseModelTag(tag);

            if (engine == "parakeet")
            {
                var model = ParakeetModelInfo.AllModels.FirstOrDefault(m => m.Id == modelId);
                if (model != null && !model.IsEnglishOnly)
                {
                    // Multilingual Parakeet: filter to supported languages
                    var supportedCodes = model.SupportedLanguages;
                    var filteredLanguages = LanguageInfo.AllLanguages.Where(lang =>
                        lang.Code == "auto" || supportedCodes.Contains(lang.Code, StringComparer.OrdinalIgnoreCase));
                    ReplaceLanguageItems(filteredLanguages);
                    return;
                }
                // English-only Parakeet: language picker is disabled by AutoSelectEnglishForModel,
                // but keep the full list so switching back to a multilingual model works
            }
        }

        if (providerType == "cloud")
        {
            // HyperWhisper Cloud is a tier wrapper: resolve the routed upstream
            // provider + model so the same per-provider language filtering the
            // BYOK branches use also runs for the cloud tier; otherwise the picker
            // shows the full list and lets the user save a language the routed
            // model can't handle. `isHyperWhisperCloud` selects the catalog-driven
            // fallback filter below for tiers without a dedicated branch here.
            // HyperWhisper Cloud is detected from the SOURCE segment, not the BYOK
            // provider combo — that combo no longer carries a "hyperwhisper" entry
            // (HW Cloud has its own source segment), so reading its tag would always
            // miss and skip every tier-specific filter below.
            ResolveEffectiveCloudProviderAndModel(out var cloudProvider, out var effectiveModelId);

            if (cloudProvider == CloudTranscriptionProvider.Soniox)
            {
                var supportedCodes = new HashSet<string>(LanguageInfo.SonioxAsyncLanguageCodes, StringComparer.OrdinalIgnoreCase);
                var filteredLanguages = LanguageInfo.AllLanguages.Where(lang => supportedCodes.Contains(lang.Code));
                ReplaceLanguageItems(filteredLanguages);
                return;
            }

            if (cloudProvider == CloudTranscriptionProvider.AssemblyAI)
            {
                var modelId = effectiveModelId;
                if (modelId == "dictation")
                {
                    var support = Services.SharedModelsCatalog.GetLanguageSupport("assemblyAI", CatalogKind.Voice, "dictation");
                    ReplaceLanguageItems(support.Codes.Select(code => new LanguageInfo(code, LanguageInfo.GetDisplayName(code))));
                    return;
                }

                // For the HyperWhisper Cloud AssemblyAI tier, Medical Mode is the
                // separate MedicalDomainCheck (X-STT-Domain), not a "-medical"
                // model id like the BYOK variants. Fold the checked domain into the
                // effective model id so the medical-language restriction below fires
                // for both encodings. Gate on isHyperWhisperCloud (mirrors macOS's
                // currentCloudProvider == .hyperwhisper guard): the checkbox state
                // leaks when the outer provider is switched to BYOK "assemblyai"
                // (the BYOK panel collapses but doesn't hide/uncheck it), so without
                // this gate BYOK AssemblyAI would be wrongly clamped to the 5
                // medical languages.

                string[]? filteredCodes = null;

                // Medical Mode add-on only supports EN/ES/DE/FR — for other
                // languages AssemblyAI silently skips the domain correction.
                if (modelId.EndsWith("-medical", StringComparison.Ordinal))
                {
                    filteredCodes = new[] { "auto", "en", "es", "de", "fr" };
                }
                if (filteredCodes != null)
                {
                    var supportedCodes = new HashSet<string>(filteredCodes, StringComparer.OrdinalIgnoreCase);
                    var filteredLanguages = LanguageInfo.AllLanguages.Where(lang => supportedCodes.Contains(lang.Code));
                    ReplaceLanguageItems(filteredLanguages);
                    return;
                }
            }

            if (cloudProvider == CloudTranscriptionProvider.Grok)
            {
                var filteredLanguages = LanguageInfo.AllLanguages.Where(lang =>
                    lang.Code == "auto" || GrokSttService.TryGetSupportedFormattingLanguageCode(lang.Code, out _));
                ReplaceLanguageItems(filteredLanguages);
                return;
            }

            // HyperWhisper Cloud Deepgram tier: the medical models
            // (nova-3-medical / nova-2-medical) are ENGLISH-ONLY (vendor-confirmed),
            // unlike the broad language set of the general models. The catalog-codes
            // fallback below keys on the TIER, not the model, so restrict to English
            // here before it runs. Gated on isHyperWhisperCloud (mirrors the
            // AssemblyAI-medical branch) so a leaked picker state can't clamp a BYOK
            // Deepgram selection.

            // HyperWhisper Cloud Azure MAI tier: the two models do NOT share a
            // language set. mai-transcribe-2 documents 60 locales, 1.5 only 42,
            // and `cloud-stt-catalog.json`'s provider-level `languages.codes` is
            // their UNION — so the tier-keyed fallback below would offer a 1.5
            // user the 18 codes only v2 has, and Azure would silently fall back
            // to auto-detect on each of them. The per-model split lives in
            // `shared-models/models-catalog.json` (`supportedLanguages`), which
            // is the same data macOS reads through STTLanguageTemplates; reading
            // it over the FFI here keeps the two heads on one source rather than
            // retyping the list.
            //
            // Deliberately scoped to Azure. The tier branch below is NOT
            // generalised to per-model for every provider: nova-3-medical's
            // English-only clamp already sits above with its own vendor-confirmed
            // reasoning, and no other tier's per-model `supportedLanguages` rows
            // have been validated against these branches.

            // HyperWhisper Cloud tiers without a dedicated branch above
            // (Deepgram, OpenAI, ElevenLabs, …): filter to the routed tier's
            // catalog-declared languages. The catalog stores upstream-native codes
            // in mixed formats (BCP-47 en-AU, ISO-639-2/3 eng, region variants
            // ar-AE), so we normalize to the picker's two-letter base via
            // PickerLanguageCodesForId before intersecting — intersecting the raw
            // codes would collapse e.g. ElevenLabs/Chirp to a 2-item list. Unknown
            // ("unverified") sets return null and fall through to the full list.

        }

        // Whisper or Cloud: restore full language list if it was filtered
        if (LanguageCombo.Items.Count < LanguageInfo.AllLanguages.Length)
        {
            ReplaceLanguageItems(LanguageInfo.AllLanguages);
        }
    }

    private void SelectLanguage(string languageCode)
    {
        foreach (ComboBoxItem item in LanguageCombo.Items)
        {
            if (item.Tag?.ToString() == languageCode)
            {
                LanguageCombo.SelectedItem = item;
                break;
            }
        }
    }
}
