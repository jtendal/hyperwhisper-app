
using System.Net.Http;
using HyperWhisper.Data.Entities;
using HyperWhisper.Models;

namespace HyperWhisper.Services.Transcription;

/// <summary>
/// Factory for creating and configuring transcription providers.
/// Uses lazy initialization to create providers only when first accessed.
/// </summary>
public class TranscriptionProviderFactory : IDisposable
{
    // =========================================================================
    // LAZY CLOUD PROVIDER INSTANCES
    // =========================================================================

    private readonly Lazy<OpenAIWhisperService> _openAI;
    private readonly Lazy<GroqWhisperService> _groq;
    private readonly Lazy<DeepgramService> _deepgram;
    private readonly Lazy<AssemblyAIService> _assemblyAI;
    private readonly Lazy<ElevenLabsService> _elevenLabs;
    private readonly Lazy<MistralService> _mistral;
    private readonly Lazy<SonioxService> _soniox;
    private readonly Lazy<GeminiTranscriptionService> _gemini;
    private readonly Lazy<GeminiTranscribeService> _geminiTranscribe;
    private readonly Lazy<MetaMuseService> _meta;

    private readonly Lazy<GrokSttService> _grok;

    private readonly Func<CloudTranscriptionProvider, string?> _apiKeyLookup;

    private bool _disposed;

    // =========================================================================
    // CONSTRUCTOR
    // =========================================================================

    public TranscriptionProviderFactory()
        : this(GetApiKeyForProvider, httpHandler: null)
    {
    }

    /// <summary>
    /// Test seam: supply the API keys and a handler that fakes every BYOK
    /// vendor, so a test can drive the real factory and the real services
    /// without the Credential Manager or the network.
    /// </summary>
    internal TranscriptionProviderFactory(
        Func<CloudTranscriptionProvider, string?> apiKeyLookup,
        HttpMessageHandler? httpHandler)
    {
        _apiKeyLookup = apiKeyLookup ?? throw new ArgumentNullException(nameof(apiKeyLookup));

        // Lazy initialization - providers created only when first accessed
        _openAI = new Lazy<OpenAIWhisperService>(() => new OpenAIWhisperService(httpHandler));
        _groq = new Lazy<GroqWhisperService>(() => new GroqWhisperService(httpHandler));
        _deepgram = new Lazy<DeepgramService>(() => new DeepgramService(httpHandler));
        _assemblyAI = new Lazy<AssemblyAIService>(() => new AssemblyAIService(httpHandler));
        _elevenLabs = new Lazy<ElevenLabsService>(() => new ElevenLabsService(httpHandler));
        _mistral = new Lazy<MistralService>(() => new MistralService(httpHandler));
        _soniox = new Lazy<SonioxService>(() => new SonioxService(httpHandler));
        _gemini = new Lazy<GeminiTranscriptionService>(() => new GeminiTranscriptionService(httpHandler));
        _geminiTranscribe = new Lazy<GeminiTranscribeService>(() => new GeminiTranscribeService(httpHandler));
        _meta = new Lazy<MetaMuseService>(() => new MetaMuseService(httpHandler));

        _grok = new Lazy<GrokSttService>(() => new GrokSttService(httpHandler));

        LoggingService.Debug("TranscriptionProviderFactory: Initialized (providers will be created on first use)");
    }
    public ITranscriptionProvider GetConfiguredCloudProvider(
        CloudTranscriptionProvider providerType,
        string? modelId = null)
    {
        if (!providerType.IsAvailable())
            throw new TranscriptionException(TranscriptionErrorCode.ApiKeyMissing,
                "This provider is no longer available. Select a local model or configure your own API provider.", providerType.ToString());
        // Get API key (validates for providers that require it)
        string? apiKey = _apiKeyLookup(providerType);

        if (providerType.RequiresApiKey() && string.IsNullOrEmpty(apiKey))
        {
            throw new TranscriptionException(
                TranscriptionErrorCode.ApiKeyMissing,
                $"API key not configured for {providerType.GetDisplayName()}",
                providerType.GetDisplayName());
        }

        var effectiveModelId = ResolveModelId(providerType, modelId);

        // Configure and return the provider. The BYOK arms take the API key
        // only: the model is per call and travels in the TranscriptionRequest.
        return providerType switch
        {
            CloudTranscriptionProvider.OpenAI => ConfigureAndReturn(_openAI.Value, apiKey!),
            CloudTranscriptionProvider.Groq => ConfigureAndReturn(_groq.Value, apiKey!),
            CloudTranscriptionProvider.Deepgram => ConfigureAndReturn(_deepgram.Value, apiKey!),
            CloudTranscriptionProvider.AssemblyAI => ConfigureAndReturn(_assemblyAI.Value, apiKey!),
            CloudTranscriptionProvider.ElevenLabs => ConfigureAndReturn(_elevenLabs.Value, apiKey!),
            CloudTranscriptionProvider.Mistral => ConfigureAndReturn(_mistral.Value, apiKey!),
            CloudTranscriptionProvider.Soniox => ConfigureAndReturn(_soniox.Value, apiKey!),
            CloudTranscriptionProvider.Gemini => ConfigureAndReturn(_gemini.Value, apiKey!),
            CloudTranscriptionProvider.GeminiTranscribe => ConfigureAndReturn(_geminiTranscribe.Value, apiKey!),
            CloudTranscriptionProvider.Meta => ConfigureAndReturn(_meta.Value, apiKey!),

            CloudTranscriptionProvider.Grok => ConfigureAndReturn(_grok.Value, apiKey!),

            _ => throw new ArgumentException($"Unknown cloud provider: {providerType}")
        };
    }

    /// <summary>
    /// The model id one call should run: the mode's own, or the provider's
    /// catalog default when the mode names none. The orchestrator puts the
    /// result into the call's <see cref="TranscriptionRequest"/>.
    /// </summary>
    public static string ResolveModelId(CloudTranscriptionProvider providerType, string? modelId)
    {
        // GetDefault reads shared-app-classification/cloud-stt-catalog.json
        // through the shared core (issue #580). A second, hand-written fallback
        // table used to sit behind it here, holding its own copy of the same
        // default column — that is precisely the split #580 closes, so it is
        // gone. GetDefault returns null only for a provider that has no models
        // in the picker at all (CloudTranscriptionProvider.None, or one added to
        // the enum but not to the catalog), and for that case the empty model id
        // is the honest answer: guessing some other vendor's model id would send
        // a request that is wrong rather than one that is merely unconfigured.
        return modelId
            ?? CloudTranscriptionModels.GetDefault(providerType)?.Id
            ?? string.Empty;
    }

    /// <summary>
    /// Gets display name for cloud provider.
    /// </summary>
    public static string GetProviderDisplayName(CloudTranscriptionProvider provider, string? modelId)
    {
        // Return only provider name (consistent with macOS app)
        // Model name is configuration detail, not relevant for history display
        return provider.GetDisplayName();
    }
    public static string? GetApiKeyForProvider(CloudTranscriptionProvider provider)
    {
        return provider switch
        {
            // Shared keys with post-processing
            CloudTranscriptionProvider.OpenAI => ApiKeyService.Instance.GetApiKey(PostProcessingProvider.OpenAI),
            CloudTranscriptionProvider.Groq => ApiKeyService.Instance.GetApiKey(PostProcessingProvider.Groq),
            CloudTranscriptionProvider.Gemini => ApiKeyService.Instance.GetApiKey(PostProcessingProvider.Gemini),
            CloudTranscriptionProvider.Grok => ApiKeyService.Instance.GetApiKey(PostProcessingProvider.Grok),

            // Transcription-only providers
            CloudTranscriptionProvider.Deepgram => ApiKeyService.Instance.GetApiKey(TranscriptionApiKeyType.Deepgram),
            CloudTranscriptionProvider.AssemblyAI => ApiKeyService.Instance.GetApiKey(TranscriptionApiKeyType.AssemblyAI),
            CloudTranscriptionProvider.ElevenLabs => ApiKeyService.Instance.GetApiKey(TranscriptionApiKeyType.ElevenLabs),
            CloudTranscriptionProvider.Mistral => ApiKeyService.Instance.GetApiKey(TranscriptionApiKeyType.Mistral),
            CloudTranscriptionProvider.Soniox => ApiKeyService.Instance.GetApiKey(TranscriptionApiKeyType.Soniox),
            // Own key slot — deliberately NOT PostProcessingProvider.Gemini.
            CloudTranscriptionProvider.GeminiTranscribe => ApiKeyService.Instance.GetApiKey(TranscriptionApiKeyType.GeminiTranscribe),
            CloudTranscriptionProvider.Meta => ApiKeyService.Instance.GetApiKey(TranscriptionApiKeyType.Meta),

            // HyperWhisper-Cloud-routed providers don't need an API key
            CloudTranscriptionProvider.HyperWhisperCloud => null,
            CloudTranscriptionProvider.MicrosoftAzureSpeech => null,
            CloudTranscriptionProvider.GoogleSpeech => null,

            _ => null
        };
    }

    // =========================================================================
    // PRIVATE HELPERS
    // =========================================================================

    // One overload covers every API-key provider: they all derive from
    // ApiKeyTranscriptionServiceBase, which declares Configure. It writes the
    // API key only — per-profile state. It must never write per-call state
    // (the model, a prompt): this instance is shared by every overlapping
    // transcription (issue #753).
    private static ITranscriptionProvider ConfigureAndReturn(ApiKeyTranscriptionServiceBase service, string apiKey)
    {
        service.Configure(apiKey);
        return service;
    }

    // =========================================================================
    // DISPOSAL
    // =========================================================================

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Only dispose providers that were actually created
        SafeDispose(_openAI);
        SafeDispose(_groq);
        SafeDispose(_deepgram);
        SafeDispose(_assemblyAI);
        SafeDispose(_elevenLabs);
        SafeDispose(_mistral);
        SafeDispose(_soniox);
        SafeDispose(_gemini);
        SafeDispose(_geminiTranscribe);
        SafeDispose(_meta);

        SafeDispose(_grok);

        LoggingService.Debug("TranscriptionProviderFactory: Disposed");
        GC.SuppressFinalize(this);
    }

    private static void SafeDispose<T>(Lazy<T> lazy) where T : IDisposable
    {
        if (lazy.IsValueCreated)
        {
            try { lazy.Value.Dispose(); }
            catch (Exception ex) { LoggingService.Warn($"Dispose failed for {typeof(T).Name}: {ex.Message}"); }
        }
    }
}
