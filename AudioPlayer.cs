using System;
using System.IO;
using NAudio.Wave;

namespace SCLOCUA
{
    public sealed class AudioPlayer : IDisposable
    {
        private readonly string _filePath;
        private IWavePlayer _output;
        private AudioFileReader _reader;
        private bool _isPlaying;

        public AudioPlayer(string filePath)
        {
            _filePath = filePath;
        }

        public void Play()
        {
            if (_isPlaying) return;
            if (!File.Exists(_filePath)) return;

            Cleanup();
            _reader = new AudioFileReader(_filePath);
            _output = new WaveOutEvent();
            _output.Init(_reader);
            _output.PlaybackStopped += (_, __) => { _isPlaying = false; Cleanup(); };
            _output.Play();
            _isPlaying = true;
        }

        public void Stop()
        {
            try { _output?.Stop(); } catch { }
            _isPlaying = false;
            Cleanup();
        }

        private void Cleanup()
        {
            _output?.Dispose();
            _reader?.Dispose();
            _output = null;
            _reader = null;
        }

        public void Dispose() => Stop();
    }
}
