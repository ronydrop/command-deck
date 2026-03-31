using System;
using System.Globalization;
using System.Speech.Recognition;
using System.Threading.Tasks;

namespace CommandDeck.Services;

/// <summary>
/// Voice input service using Windows Speech Recognition (System.Speech).
/// Provides continuous dictation with real-time partial results.
/// </summary>
public class VoiceInputService : IVoiceInputService, IDisposable
{
    private SpeechRecognitionEngine? _engine;
    private bool _isRecording;
    private string _currentTranscript = string.Empty;
    private bool _disposed;
    private bool? _isAvailableCache; // cached to avoid repeated hardware probing

    public event Action<string>? TranscriptionUpdated;
    public event Action<string>? TranscriptionCompleted;

    public bool IsRecording => _isRecording;

    public bool IsAvailable
    {
        get
        {
            if (_isAvailableCache.HasValue) return _isAvailableCache.Value;
            try
            {
                // Probe once — instantiating SpeechRecognitionEngine accesses audio hardware
                using var test = new SpeechRecognitionEngine(CultureInfo.CurrentCulture);
                _isAvailableCache = true;
                return true;
            }
            catch
            {
                _isAvailableCache = false;
                return false;
            }
        }
    }

    public Task StartRecordingAsync()
    {
        if (_isRecording) return Task.CompletedTask;

        try
        {
            _currentTranscript = string.Empty;
            _engine = new SpeechRecognitionEngine(CultureInfo.CurrentCulture);

            // Dictation grammar recognizes free-form speech
            _engine.LoadGrammar(new DictationGrammar());

            _engine.SpeechRecognized += OnSpeechRecognized;
            _engine.SpeechHypothesized += OnSpeechHypothesized;
            _engine.SpeechRecognitionRejected += OnSpeechRejected;

            _engine.SetInputToDefaultAudioDevice();
            _engine.RecognizeAsync(RecognizeMode.Multiple);

            _isRecording = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VoiceInput] StartRecording failed: {ex}");
        }

        return Task.CompletedTask;
    }

    public Task<string> StopAndTranscribeAsync()
    {
        if (!_isRecording) return Task.FromResult(_currentTranscript);

        try
        {
            _isRecording = false;
            _engine?.RecognizeAsyncStop();

            var final = _currentTranscript.Trim();
            TranscriptionCompleted?.Invoke(final);
            return Task.FromResult(final);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VoiceInput] StopRecording failed: {ex}");
            return Task.FromResult(_currentTranscript);
        }
        finally
        {
            DisposeEngine();
        }
    }

    private void OnSpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        if (e.Result?.Text is null) return;
        _currentTranscript += e.Result.Text + " ";
        TranscriptionUpdated?.Invoke(_currentTranscript.Trim());
    }

    private void OnSpeechHypothesized(object? sender, SpeechHypothesizedEventArgs e)
    {
        if (e.Result?.Text is null) return;
        // Show hypothesis as partial (gray) text
        var partial = _currentTranscript + e.Result.Text + "…";
        TranscriptionUpdated?.Invoke(partial.Trim());
    }

    private void OnSpeechRejected(object? sender, SpeechRecognitionRejectedEventArgs e)
    {
        // Nothing to do — just continue listening
    }

    private void DisposeEngine()
    {
        if (_engine is null) return;
        _engine.SpeechRecognized -= OnSpeechRecognized;
        _engine.SpeechHypothesized -= OnSpeechHypothesized;
        _engine.SpeechRecognitionRejected -= OnSpeechRejected;
        _engine.Dispose();
        _engine = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _isRecording = false;
        DisposeEngine();
    }
}
