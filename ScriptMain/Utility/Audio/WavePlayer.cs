using GTA;
using GTA.Math;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using TornadoScript.ScriptCore;
using TornadoScript.ScriptCore.Game;

namespace TornadoScript.ScriptMain.Utility
{
    public class WavePlayer : IDisposable
    {
        private float currentVolume = 0.0f;

        private float fadeTarget = 0.0f;

        private int fadeTime = 0;

        private bool soundFadingIn = false, soundFadingOut = false;

        private LoopStream _waveStream;

        private SampleChannel _waveChannel;

        private WaveOutEvent _waveOut;

        private Vector3 _position;
        private float _maxDistance = 100.0f;
        private float _minDistance = 5.0f;

        private bool _disposed;
        private readonly object _disposeLock = new object();

        public WavePlayer(string audioFilename)
        {
            _waveStream = new LoopStream(new WaveFileReader(audioFilename));

            _waveChannel = new SampleChannel(_waveStream);

            _waveOut = new WaveOutEvent();

            _waveOut.Init(_waveChannel);

            var soundManager = ScriptThread.GetOrCreate<SoundManager>();

            soundManager.Add(this);
        }

        public void SetVolume(float volumeLevel)
        {
            //   GTA.UI.ShowSubtitle(_waveOut.Volume.ToString());
            if (soundFadingIn || soundFadingOut)
                return;
            _waveChannel.Volume = volumeLevel;
        }

        public bool IsPlaying()
        {
            return _waveOut.PlaybackState == PlaybackState.Playing;
        }

        public bool IsPaused()
        {
            return _waveOut.PlaybackState == PlaybackState.Paused;
        }

        public void SetLoopAudio(bool shouldLoopAudio)
        {
            _waveStream.EnableLooping = shouldLoopAudio;
        }

        public void DoFadeIn(int fadeTime, float fadeTarget)
        {
            this.fadeTime = fadeTime;
            this.fadeTarget = fadeTarget;
            soundFadingOut = false;
            soundFadingIn = true;
            currentVolume = _waveChannel.Volume;
            _waveOut.Play();
        }

        public void DoFadeOut(int fadeTime, float fadeTarget)
        {
            this.fadeTime = fadeTime;
            this.fadeTarget = fadeTarget;
            soundFadingOut = true;
            soundFadingIn = false;
            currentVolume = _waveChannel.Volume;
            _waveOut.Play();
        }

        public void Pause()
        {
            _waveOut.Pause();
        }

        public void Stop()
        {
            _waveOut.Stop();
        }

        public void Play(bool fromStart = false)
        {
            if (fromStart)
                _waveStream.CurrentTime = System.TimeSpan.Zero;

            _waveOut.Play();
        }

        public void Update()
        {
            if (soundFadingIn)
            {
                if (currentVolume < fadeTarget)
                {
                    currentVolume += GTA.Game.LastFrameTime * (1000.0f / fadeTime);

                    currentVolume = currentVolume < 0.0f ? 0.0f : currentVolume > 1.0f ? 1.0f : currentVolume;

                    _waveChannel.Volume = currentVolume;
                }

                else
                    soundFadingIn = false;
            }

            else if (soundFadingOut)
            {
                if (currentVolume > fadeTarget)
                {
                    currentVolume -= GTA.Game.LastFrameTime * (1000.0f / fadeTime);

                    currentVolume = currentVolume < 0.0f ? 0.0f : currentVolume > 1.0f ? 1.0f : currentVolume;

                    _waveChannel.Volume = currentVolume;
                }

                else
                {
                    _waveOut.Stop();

                    soundFadingOut = false;
                }
            }

            // Update 3D positioning
            UpdateVolume();
        }

        public void SetPosition(Vector3 position)
        {
            _position = position;
            UpdateVolume();
        }

        public void SetDistanceParameters(float minDistance, float maxDistance)
        {
            _minDistance = minDistance;
            _maxDistance = maxDistance;
            UpdateVolume();
        }

        private void UpdateVolume()
        {
            if (!IsPlaying()) return;

            var playerPos = Game.Player.Character.Position;
            var distance = Vector3.Distance(playerPos, _position);

            // Calculate volume based on distance
            float volume = 1.0f;
            if (distance > _minDistance)
            {
                volume = System.Math.Max(0.0f, 1.0f - ((distance - _minDistance) / (_maxDistance - _minDistance)));
            }

            // Apply volume with smooth interpolation
            float currentVolume = _waveChannel.Volume;
            float targetVolume = volume;
            float smoothing = 0.1f;
            _waveChannel.Volume = currentVolume + (targetVolume - currentVolume) * smoothing;
        }

        public void OnUpdate(int gameTime)
        {
            Update();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            lock (_disposeLock)
            {
                if (_disposed) return;

                if (disposing)
                {
                    try
                    {
                        _waveOut?.Stop();
                        _waveOut?.Dispose();
                        _waveStream?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error disposing WavePlayer: {ex.Message}");
                    }
                }

                _disposed = true;
            }
        }

        ~WavePlayer()
        {
            Dispose(false);
        }
    }
}
