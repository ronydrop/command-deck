using System;
using System.Threading.Tasks;

namespace CommandDeck.Services;

/// <summary>
/// Provides voice capture and transcription for the AI Orb.
/// Uses Windows Speech Recognition (System.Speech) for real-time dictation.
/// </summary>
public interface IVoiceInputService
{
    /// <summary>Fired when a partial transcription is available (real-time feedback).</summary>
    event Action<string>? TranscriptionUpdated;

    /// <summary>Fired when transcription is complete (user stopped recording).</summary>
    event Action<string>? TranscriptionCompleted;

    /// <summary>True while voice capture is active.</summary>
    bool IsRecording { get; }

    /// <summary>True if voice input is available on this system.</summary>
    bool IsAvailable { get; }

    /// <summary>Starts capturing voice input.</summary>
    Task StartRecordingAsync();

    /// <summary>Stops capturing and returns the final transcription text.</summary>
    Task<string> StopAndTranscribeAsync();
}
