using System;
using System.Threading.Tasks;

namespace CommandDeck.Services;

public sealed record VoiceStartResult(bool Success, string? ErrorMessage = null);

public sealed record VoiceStopResult(
    bool Success,
    bool WasCanceled,
    string Transcript = "",
    string? ErrorMessage = null);

/// <summary>
/// Provides voice capture and transcription for the AI Orb.
/// Uses Windows Speech Recognition (System.Speech) for real-time dictation.
/// </summary>
public interface IVoiceInputService
{
    /// <summary>Fired when a partial transcription is available (real-time feedback).</summary>
    event Action<string>? TranscriptionUpdated;

    /// <summary>True while voice capture is active.</summary>
    bool IsRecording { get; }

    /// <summary>True if voice input is available on this system.</summary>
    bool IsAvailable { get; }

    /// <summary>Starts capturing voice input.</summary>
    Task<VoiceStartResult> StartRecordingAsync();

    /// <summary>Stops capturing and returns the final transcription text.</summary>
    Task<VoiceStopResult> StopRecordingAsync();

    /// <summary>Cancels the current capture without emitting a final transcription.</summary>
    Task<VoiceStopResult> CancelRecordingAsync();
}
