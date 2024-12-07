using GTA;
using GTA.UI;
using System;
using System.Collections.Generic;
using System.Drawing;
using TornadoScript.ScriptCore;

namespace TornadoScript.ScriptMain.Frontend
{
    public class FrontendOutput : IDisposable
    {
        private const int MaxVisibleLines = 10;  // Maximum number of visible lines in console
        private const int TextActiveTime = 5000;
        private const int LineHeight = 20;    // Vertical space between text lines
        private const float TextScale = 0.4f; // Size of the text
        private const int TextPadding = 5;   // Padding around text
        private const int ConsoleX = 10;     // Base X position for console
        private const int ConsoleY = 10;    // Base Y position for console
        private const int TextX = ConsoleX + 10;  // Text indent from console edge
        private const int ConsoleWidth = 500;     // Width of console background
        private const int TextOffsetY = 5; // Vertical offset to position text inside console
        private const int FadeOutSpeed = 1;  // Reduced fade speed

        private readonly TextElement[] _text = new TextElement[MaxVisibleLines];  // Limit array size to max visible lines
        private readonly ContainerElement _backsplash;
        private readonly FrontendOutputCore _output;
        private bool _stayOnScreen;
        private int _shownTime;
        private bool _disposed;
        private bool _needsTextUpdate = true;
        private bool _isBacksplashDisposed;
        private readonly object _updateLock = new object();

        public FrontendOutput()
        {
            _output = new FrontendOutputCore();

            // Position console with consistent spacing
            _backsplash = new ContainerElement(
                new Point(ConsoleX, ConsoleY),
                new Size(ConsoleWidth, LineHeight * MaxVisibleLines + TextPadding * 2),
                Color.FromArgb(180, 0, 0, 0)
            );
            CreateText();
        }

        private void CreateText()
        {
            for (var i = 0; i < _text.Length; i++)
            {
                _text[i] = new TextElement(
                    string.Empty,
                    new Point(TextX, ConsoleY + TextOffsetY + (LineHeight * i)),
                    TextScale,
                    Color.FromArgb(255, 255, 255, 255),
                    GTA.UI.Font.ChaletLondon,
                    GTA.UI.Alignment.Left
                );
                _backsplash.Items.Add(_text[i]);
            }
        }

        public void Update(int gameTime)
        {
            if (_disposed) return;

            lock (_updateLock)
            {
                if (_needsTextUpdate)
                {
                    UpdateVisibleText();
                    _needsTextUpdate = false;
                }

                // Add fade-out logic using _stayOnScreen
                if (!_stayOnScreen && gameTime - _shownTime > TextActiveTime)
                {
                    // Gradually fade out
                    var currentAlpha = _backsplash.Color.A;
                    if (currentAlpha > 0)
                    {
                        var newAlpha = Math.Max(0, currentAlpha - FadeOutSpeed);
                        _backsplash.Color = Color.FromArgb(newAlpha, 0, 0, 0);
                        
                        // Also fade text
                        foreach (var text in _text)
                        {
                            text.Color = Color.FromArgb(newAlpha, 255, 255, 255);
                        }
                    }
                }

                if (!_isBacksplashDisposed && _backsplash != null)
                {
                    _backsplash.Draw();
                }
            }
        }

        private void UpdateVisibleText()
        {
            // Clear all text elements first
            for (var i = 0; i < _text.Length; i++)
            {
                _text[i].Caption = string.Empty;
            }

            // Update with current visible lines
            var currentLines = _output.GetLines();
            for (var i = 0; i < currentLines.Count && i < MaxVisibleLines; i++)
            {
                _text[i].Caption = currentLines[i];
            }
        }

        public void WriteLine(string line, bool timestamp = true, bool log = true)
        {
            ThrowIfDisposed();
            Show();

            // Log if requested
            if (log)
            {
                Logger.Log(line);
            }

            // Add timestamp if requested (shorter format)
            if (timestamp)
            {
                line = $"[{DateTime.Now:HH:mm}] {line}";
            }

            // Trim long lines
            if (line.Length > 60)
            {
                line = line.Substring(0, 57) + "...";
            }

            _output.AddLine(line);
            _needsTextUpdate = true;  // Mark for update

            // Make sure text is visible
            _backsplash.Color = Color.FromArgb(180, 0, 0, 0);
            foreach (var textElement in _text)
            {
                textElement.Color = Color.FromArgb(255, 255, 255, 255);
            }

            _shownTime = Game.GameTime;
            _stayOnScreen = true;  // Ensure message stays until explicitly cleared
        }

        public void Show()
        {
            ThrowIfDisposed();
            lock (_updateLock)
            {
                _backsplash.Color = Color.FromArgb(180, 0, 0, 0);
                _stayOnScreen = true;
                UpdateVisibleText();
                _needsTextUpdate = false;
            }
            Logger.Debug("Console output shown");
        }

        public void Hide()
        {
            ThrowIfDisposed();
            lock (_updateLock)
            {
                Clear();
                _stayOnScreen = false;
                _backsplash.Color = Color.FromArgb(0, 0, 0, 0);
            }
            Logger.Debug("Console output hidden");
        }

        public void EnableFadeOut()
        {
            ThrowIfDisposed();
            _stayOnScreen = false;
        }

        public void Clear()
        {
            ThrowIfDisposed();
            _output.Clear();
            
            // Keep console visible and restore text color
            _backsplash.Color = Color.FromArgb(180, 0, 0, 0);
            foreach (var text in _text)
            {
                text.Color = Color.FromArgb(255, 255, 255, 255);
            }
            
            _stayOnScreen = true;  // Keep the console visible
            UpdateVisibleText();
            Logger.Debug("Console output cleared but keeping console visible");
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(FrontendOutput));
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed resources
                    if (_backsplash is IDisposable backsplashDisposable)
                    {
                        backsplashDisposable.Dispose();
                    }

                    if (_text != null)
                    {
                        foreach (var textElement in _text)
                        {
                            if (textElement is IDisposable textDisposable)
                            {
                                textDisposable.Dispose();
                            }
                        }
                    }

                    // Clear message queue and reset state
                    Clear();
                    Hide();
                    _output.Dispose();
                    _isBacksplashDisposed = true; // Track that the backsplash is disposed
                }

                _disposed = true;
            }
        }

        ~FrontendOutput()
        {
            Dispose(false);
        }
    }

    public class FrontendOutputCore : IDisposable
    {
        private readonly List<string> _outputLines = new List<string>();
        private readonly object _outputLock = new object();
        private readonly int _maxLines;
        private bool _disposed;

        public FrontendOutputCore(int maxLines = 100)
        {
            _maxLines = maxLines;
        }

        public void AddLine(string line)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(line)) return;

            lock (_outputLock)
            {
                _outputLines.Add(line);
                while (_outputLines.Count > _maxLines)
                {
                    _outputLines.RemoveAt(0);
                }
            }
        }

        public void Clear()
        {
            ThrowIfDisposed();
            lock (_outputLock)
            {
                _outputLines.Clear();
            }
        }

        public IReadOnlyList<string> GetLines()
        {
            ThrowIfDisposed();
            lock (_outputLock)
            {
                return _outputLines.AsReadOnly();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(FrontendOutputCore));
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // Clear all lines when disposing
                Clear();
            }

            _disposed = true;
        }

        ~FrontendOutputCore()
        {
            Dispose(false);
        }
    }
}