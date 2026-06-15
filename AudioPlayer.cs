using System;
using System.IO;
using System.Threading;
using NAudio.Wave;

namespace SCLOCUA
{
    /// <summary>
    /// Thread-safe audio player with proper RCW lifecycle management.
    ///
    /// KEY ARCHITECTURE:
    /// - WaveOutEvent.Stop() is ASYNC - it doesn't immediately stop the audio thread
    /// - PlaybackStopped callback is the ONLY safe place to dispose COM objects
    /// - Stop() does NOT block - it just signals stop
    /// - Dispose() blocks briefly to ensure cleanup happens
    ///
    /// This prevents RaceOnRCWCleanup by ensuring audio thread is fully stopped
    /// before any COM objects (RCWs) are disposed.
    /// </summary>
    public sealed class AudioPlayer : IDisposable
    {
        private readonly string _filePath;
        private readonly object _lock = new object();
        private IWavePlayer _output;
        private AudioFileReader _reader;
        private int _isPlaying;          // 0 = stopped, 1 = playing (Interlocked)
        private int _isDisposed;         // 0 = active, 1 = disposed (Interlocked)
        private int _cleanupCompleted;   // 0 = not cleaned, 1 = cleaned (Interlocked)
        private ManualResetEventSlim _playbackStoppedEvent;  // Synchronization event

        public AudioPlayer(string filePath)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        }

        /// <summary>
        /// Starts audio playback.
        /// Idempotent: safe to call multiple times.
        /// </summary>
        public void Play()
        {
            // Fast atomic check: if already playing, return immediately without lock
            if (Interlocked.CompareExchange(ref _isPlaying, 1, 0) == 1)
                return; // Already playing

            lock (_lock)
            {
                // Double-check inside lock
                if (_isDisposed == 1)
                {
                    Interlocked.Exchange(ref _isPlaying, 0);
                    return; // Don't play if disposed
                }

                try
                {
                    // Clean up any PREVIOUS playback resources
                    // Safe because we're creating NEW resources
                    CleanupResourcesUnsafe();

                    // Verify file exists
                    if (!File.Exists(_filePath))
                    {
                        Interlocked.Exchange(ref _isPlaying, 0);
                        return;
                    }

                    // Reset cleanup completed flag
                    Interlocked.Exchange(ref _cleanupCompleted, 0);

                    // Create synchronization event
                    _playbackStoppedEvent = new ManualResetEventSlim(false);

                    // Create new audio playback objects
                    _reader = new AudioFileReader(_filePath);
                    _output = new WaveOutEvent();
                    _output.Init(_reader);

                    // Subscribe BEFORE playing
                    _output.PlaybackStopped += OnPlaybackStopped;
                    _output.Play();
                }
                catch
                {
                    // On any error, clean up and reset
                    CleanupResourcesUnsafe();
                    Interlocked.Exchange(ref _isPlaying, 0);
                    Interlocked.Exchange(ref _cleanupCompleted, 1);
                }
            }
        }

        /// <summary>
        /// Stops audio playback.
        /// NON-BLOCKING: Returns immediately, audio stops asynchronously.
        /// Idempotent: safe to call multiple times.
        /// </summary>
        public void Stop()
        {
            lock (_lock)
            {
                if (_isPlaying == 0)
                    return; // Already stopped

                Interlocked.Exchange(ref _isPlaying, 0);

                // Tell audio output to stop (async operation)
                if (_output != null && _output.PlaybackState != PlaybackState.Stopped)
                {
                    try
                    {
                        _output.Stop();
                    }
                    catch { }
                }
            }

            // DO NOT WAIT here - return immediately to avoid blocking UI
            // Cleanup will happen in OnPlaybackStopped callback
        }

        /// <summary>
        /// Callback from NAudio's audio thread when playback ACTUALLY stops.
        /// This is the ONLY safe place to dispose RCW (COM wrapper) objects.
        /// </summary>
        private void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            // Signal that playback has stopped (outside lock to prevent deadlock)
            var stoppedEvent = _playbackStoppedEvent;
            stoppedEvent?.Set();

            // Perform cleanup (only once)
            if (Interlocked.CompareExchange(ref _cleanupCompleted, 1, 0) == 0)
            {
                lock (_lock)
                {
                    CleanupResourcesUnsafe();
                }
            }
        }

        /// <summary>
        /// Safely disposes audio resources.
        /// MUST be called from OnPlaybackStopped (after audio thread stops).
        /// NOT thread-safe - must be called inside lock.
        /// </summary>
        private void CleanupResourcesUnsafe()
        {
            // Unsubscribe from event FIRST to prevent race condition
            if (_output != null)
            {
                try
                {
                    _output.PlaybackStopped -= OnPlaybackStopped;
                }
                catch { }

                // Stop playback if not already stopped
                try
                {
                    if (_output.PlaybackState != PlaybackState.Stopped)
                        _output.Stop();
                }
                catch { }

                // NOW it's safe to dispose - audio thread is no longer using these
                try
                {
                    _output.Dispose();
                }
                catch { }

                _output = null;
            }

            // Dispose the reader
            // Safe to do now because output no longer references it
            if (_reader != null)
            {
                try
                {
                    _reader.Dispose();
                }
                catch { }

                _reader = null;
            }

            // Clear the stopped event
            if (_playbackStoppedEvent != null)
            {
                try
                {
                    _playbackStoppedEvent.Dispose();
                }
                catch { }
                _playbackStoppedEvent = null;
            }
        }

        /// <summary>
        /// Disposes the audio player.
        /// Blocks briefly to ensure cleanup happens.
        /// Idempotent: safe to call multiple times.
        /// </summary>
        public void Dispose()
        {
            // Atomic check: only dispose once
            if (Interlocked.CompareExchange(ref _isDisposed, 1, 0) != 0)
                return; // Already disposed

            // Signal stop
            Interlocked.Exchange(ref _isPlaying, 0);

            lock (_lock)
            {
                // Stop audio if playing
                if (_output != null && _output.PlaybackState != PlaybackState.Stopped)
                {
                    try
                    {
                        _output.Stop();
                    }
                    catch { }
                }
            }

            // Wait for PlaybackStopped to complete cleanup
            // This ensures audio thread has stopped BEFORE we return
            var stoppedEvent = _playbackStoppedEvent;
            if (stoppedEvent != null)
            {
                // Wait up to 1 second for playback to stop
                stoppedEvent.Wait(TimeSpan.FromSeconds(1));
            }

            // Final cleanup (in case PlaybackStopped didn't run or run yet)
            lock (_lock)
            {
                if (Interlocked.CompareExchange(ref _cleanupCompleted, 1, 0) == 0)
                {
                    CleanupResourcesUnsafe();
                }
            }
        }
    }
}
