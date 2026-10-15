// NETWORK CONFIGURATION
// Central configuration for all network-related settings and API endpoints.
// Uses conditional compilation to switch between dev/prod environments.
//
// ENVIRONMENT SWITCHING:
// - DEBUG builds: Use development endpoints (transcribe-dev-v1)
// - RELEASE builds: Use production endpoints (transcribe-prod-v2)
//
// This mirrors the macOS implementation in NetworkConfig.swift which uses
// #if DEBUG conditional compilation for the same purpose.

namespace HyperWhisper.Configuration;

/// <summary>
/// Network configuration for HyperWhisper API communication.
/// Automatically switches between development and production based on build configuration.
/// </summary>
public static class NetworkConfig
{
    public static bool IsDevelopment =>
#if DEBUG
        true;
#else
        false;
#endif

}
