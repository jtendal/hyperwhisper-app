using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using HyperWhisper.Data.Entities;
using HyperWhisper.Models;
using HyperWhisper.Services;

namespace HyperWhisper.Views.Windows;

public partial class ModeEditorWindow
{
    private void LoadModeIntoEditor(Mode mode)
    {
        _isLoading = true;

        try
        {
            ModeNameBox.Text = mode.Name;

            // Load preset (migrate legacy voiceToText)
            var presetValue = mode.Preset ?? "hyper";
            if (presetValue == "voiceToText")
                presetValue = "hyper";
            foreach (ComboBoxItem item in PresetCombo.Items)
            {
                if (item.Tag?.ToString() == presetValue)
                {
                    PresetCombo.SelectedItem = item;
                    break;
                }
            }
            if (PresetCombo.SelectedIndex == -1 && PresetCombo.Items.Count > 0)
            {
                PresetCombo.SelectedIndex = 0; // Default to Hyper
            }
            PresetDescText.Text = PresetTypeExtensions.FromString(presetValue).ToDescription();

            // Show custom instructions if preset is Custom
            CustomInstructionsPanel.Visibility = presetValue == "custom" ? Visibility.Visible : Visibility.Collapsed;
            CustomInstructionsBox.Text = mode.CustomInstructions ?? "";

            // Select saved model in unified combo using prefixed tag
            var savedEngine = mode.LocalEngine ?? "whisper";
            var savedModelId = savedEngine == "parakeet"
                ? (mode.LocalParakeetModel ?? "parakeet-v2")
                : (mode.ModelType ?? "base");
            var savedTag = $"{savedEngine}:{savedModelId}";

            bool foundLocalModel = false;
            foreach (ComboBoxItem item in LocalModelCombo.Items)
            {
                if (item.Tag?.ToString() == savedTag)
                {
                    LocalModelCombo.SelectedItem = item;
                    foundLocalModel = true;
                    break;
                }
            }
            if (!foundLocalModel && LocalModelCombo.Items.Count > 0)
            {
                LocalModelCombo.SelectedIndex = 0;
            }
            UpdateLocalModelStatus();

            // CLOUD PROVIDER AND MODEL SELECTION
            // 1. First select the cloud provider
            // 2. Then load models for that provider
            // 3. Finally select the saved model
            var cloudProvider = CloudTranscriptionProviderExtensions.FromIdentifier(mode.CloudProvider);
            var transcriptionSource = LocalModeModel.IsCloud(mode) && cloudProvider.IsAvailable()
                ? "yourprovider" : (HyperWhisper.Utilities.PlatformHelper.SupportsLocalTranscription ? "ondevice" : "yourprovider");
            var cloudProviderTag = cloudProvider.IsAvailable() ? cloudProvider.GetIdentifier() : "openai";
            foreach (ComboBoxItem item in CloudProviderCombo.Items)
                if (string.Equals(item.Tag?.ToString(), cloudProviderTag, StringComparison.OrdinalIgnoreCase))
                    CloudProviderCombo.SelectedItem = item;
            if (CloudProviderCombo.SelectedIndex < 0) CloudProviderCombo.SelectedIndex = 0;
            SetTranscriptionSourceSegment(transcriptionSource);
            UpdateSourcePanels(transcriptionSource);
            if (transcriptionSource == "yourprovider")
                LoadCloudModels(CloudTranscriptionProviderExtensions.FromIdentifier(cloudProviderTag), mode.CloudTranscriptionModel);

            // Filter language list for selected model before selecting saved language
            UpdateLanguagesForSelectedModel();

            // Load Gemini custom prompt
            var geminiPrompt = mode.GeminiCustomPrompt ?? "";
            GeminiCustomPromptBox.Text = geminiPrompt;
            GeminiCustomPromptCharCount.Text = $"{geminiPrompt.Length}/2000";
            GeminiCustomPromptPlaceholder.Visibility = string.IsNullOrEmpty(geminiPrompt)
                ? Visibility.Visible : Visibility.Collapsed;
            GeminiCustomPromptPanel.Visibility = cloudProvider == CloudTranscriptionProvider.Gemini && transcriptionSource == "yourprovider"
                ? Visibility.Visible : Visibility.Collapsed;

            bool foundLanguage = false;
            foreach (ComboBoxItem item in LanguageCombo.Items)
            {
                if (item.Tag?.ToString() == mode.Language)
                {
                    LanguageCombo.SelectedItem = item;
                    foundLanguage = true;
                    break;
                }
            }
            if (!foundLanguage && LanguageCombo.Items.Count > 0)
            {
                LanguageCombo.SelectedIndex = 0;
            }

            // Load punctuation toggles
            PunctuationCheck.IsChecked = mode.Punctuation;
            RemoveTrailingPeriodCheck.IsChecked = mode.RemoveTrailingPeriod;
            CapitalizationCheck.IsChecked = mode.Capitalization;

            PostProcessingCheck.IsChecked = mode.PostProcessingMode != 0;
            PostProcessingSettingsPanel.Visibility = mode.PostProcessingMode != 0 ? Visibility.Visible : Visibility.Collapsed;

            // Punctuation & capitalization are LLM instructions — only show when post-processing is enabled
            var ppEnabled = mode.PostProcessingMode != 0;
            PunctuationCheck.Visibility = ppEnabled ? Visibility.Visible : Visibility.Collapsed;
            CapitalizationCheck.Visibility = ppEnabled ? Visibility.Visible : Visibility.Collapsed;
            RemoveTrailingPeriodCheck.Visibility = ppEnabled
                ? (mode.Punctuation ? Visibility.Visible : Visibility.Collapsed)
                : Visibility.Visible;

            // The HyperWhisper Cloud engine/model combos are always populated from
            // the saved CloudPostProcessingModel so switching the PP source segment
            // back to HW Cloud preserves the user's selection.

            if (mode.PostProcessingMode != 0 && PostProcessingProviderExtensions.FromString(mode.PostProcessingProvider) != PostProcessingProvider.HyperWhisperCloud)
            {
                var ppProvider = PostProcessingProviderExtensions.NormalizeStorageValue(mode.PostProcessingProvider)
                    ?? PostProcessingProvider.OpenAI.ToStringValue();

                // Map the stored PP provider to one of the three source segments.
                var ppSource = ppProvider == PostProcessingProvider.LocalLlm.ToStringValue() ? "ondevice" : "yourprovider";
                SetPostProcessingSourceSegment(ppSource);

                {

                    // Local LLM is reached via the On-device segment (the provider
                    // panel stays hidden); BYOK shows the provider picker.
                    PostProcessingProviderPanel.Visibility = ppSource == "yourprovider"
                        ? Visibility.Visible : Visibility.Collapsed;
                    PostProcessingModelPanel.Visibility = Visibility.Visible;

                    if (ppSource == "yourprovider")
                    {
                        foreach (var item in PostProcessingProviderCombo.Items.OfType<ComboBoxItem>())
                        {
                            if (item.Tag?.ToString() == ppProvider)
                            {
                                PostProcessingProviderCombo.SelectedItem = item;
                                break;
                            }
                        }
                        if (PostProcessingProviderCombo.SelectedIndex == -1 && PostProcessingProviderCombo.Items.Count > 0)
                            PostProcessingProviderCombo.SelectedIndex = 0;
                    }

                    // Populate the model picker for the resolved provider (Local LLM
                    // models for On-device; the BYOK provider's models otherwise).
                    var modelProvider = ppSource == "ondevice"
                        ? PostProcessingProvider.LocalLlm
                        : PostProcessingProviderExtensions.FromString(
                            (PostProcessingProviderCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? ppProvider);
                    LoadPostProcessingModels(modelProvider);

                    var ppModelSource = ppSource == "ondevice"
                        ? mode.LocalPostProcessingModel ?? mode.LanguageModel
                        : mode.LanguageModel;
                    var ppModel = LanguageModelInfo.MigrateModelId(ppModelSource);
                    bool foundPPModel = false;
                    foreach (ComboBoxItem item in PostProcessingModelCombo.Items)
                    {
                        if (item.Tag?.ToString() == ppModel)
                        {
                            PostProcessingModelCombo.SelectedItem = item;
                            foundPPModel = true;
                            break;
                        }
                    }
                    if (!foundPPModel && PostProcessingModelCombo.Items.Count > 0)
                    {
                        PostProcessingModelCombo.SelectedIndex = 0;
                    }
                }

                // Load English spelling
                SelectEnglishSpelling(mode.EnglishSpelling);

                ProfanityFilterCheck.IsChecked = mode.ProfanityFilter;
                ScreenOCRCheck.IsChecked = mode.EnableScreenOCR;

                // Load user system prompt
                var userPrompt = mode.UserSystemPrompt ?? "";
                var hasUserPrompt = !string.IsNullOrWhiteSpace(userPrompt);
                UserPromptCheck.IsChecked = hasUserPrompt;
                UserPromptPanel.Visibility = hasUserPrompt ? Visibility.Visible : Visibility.Collapsed;
                UserPromptBox.Text = userPrompt;
                UserPromptCharCount.Text = $"{userPrompt.Length}/8000";

            }
            else
            {
                // Post-processing is off - set defaults for when it's enabled.
                // Default the source segment to HyperWhisper Cloud (or On-device if
                // HW Cloud somehow unavailable) and show its panel.
                SetPostProcessingSourceSegment("yourprovider");
                PostProcessingProviderPanel.Visibility = Visibility.Collapsed;
                PostProcessingModelPanel.Visibility = Visibility.Collapsed;

                SelectEnglishSpelling(mode.EnglishSpelling);
                ProfanityFilterCheck.IsChecked = mode.ProfanityFilter;
                ScreenOCRCheck.IsChecked = mode.EnableScreenOCR;
                UserPromptCheck.IsChecked = false;
                UserPromptPanel.Visibility = Visibility.Collapsed;
                UserPromptBox.Text = mode.UserSystemPrompt ?? "";
            }

            // Update English spelling visibility based on language
            UpdateEnglishSpellingVisibility();

            UpdateApiKeyWarning(SelectedProviderType());

            // Update vocabulary and model warnings
            UpdateAllWarnings();

            // Update language dropdown state for English-only models
            AutoSelectEnglishForModel();
        }
        finally
        {
            _isLoading = false;
        }
    }

}
