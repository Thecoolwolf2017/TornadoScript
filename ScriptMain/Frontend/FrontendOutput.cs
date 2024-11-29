using GTA;
using GTA.UI;
using System;
using System.Collections.Generic;
using System.Drawing;
using TornadoScript.ScriptCore;
using TornadoScript.ScriptMain.Script;

namespace TornadoScript.ScriptMain.Frontend
{
    public class FrontendOutput : IDisposable
    {
        private const int MaxVisibleLines = 8;  // Maximum number of visible lines in console
        private const int TextActiveTime = 10000;
        private const int LineHeight = 25;    // Vertical space between text lines
        private const float TextScale = 0.3f; // Size of the text
        private const int TextPadding = 10;   // Padding around text
        private const int ConsoleX = 20;     // Base X position for console
        private const int ConsoleY = 280;    // Base Y position for console
        private const int TextX = ConsoleX + 15;  // Text indent from console edge
        private const int ConsoleWidth = 500;     // Width of console background
        private const int TextOffsetY = -240; // Vertical offset to position text inside console

        private readonly TextElement[] _text = new TextElement[MaxVisibleLines];  // Limit array size to max visible lines
        private readonly ContainerElement _backsplash;
        private readonly Queue<string> _lines = new Queue<string>();  // Queue to store all lines
        private readonly List<DateTime> _lineTimes = new List<DateTime>();
        private bool _stayOnScreen;
        private int _shownTime;
        private bool _disposed;

        public void Clear()
        {
            ThrowIfDisposed();
            _lines.Clear();
            _lineTimes.Clear();
            UpdateVisibleText();

            // Reset visibility
            _backsplash.Color = Color.FromArgb(0, 0, 0, 0);
            _stayOnScreen = false;

            var frontendMgr = MainScript.GetOrCreate<FrontendManager>();
            if (frontendMgr != null)
            {
                frontendMgr.ResetOutputLines();
            }
        }

        public FrontendOutput()
        {
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
            ThrowIfDisposed();

            // Only fade out if not staying on screen
            if (!_stayOnScreen)
            {
                if (gameTime > _shownTime + TextActiveTime)
                {
                    if (_backsplash.Color.A > 0)
                    {
                        _backsplash.Color = Color.FromArgb(
                            Math.Max(0, _backsplash.Color.A - 2),
                            _backsplash.Color);
                    }

                    foreach (var text in _text)
                    {
                        if (text.Color.A > 0)
                        {
                            text.Color = Color.FromArgb(
                                Math.Max(0, text.Color.A - 4),
                                text.Color);
                        }
                    }
                }
            }

            UpdateVisibleText();
            _backsplash.Draw();
        }

        private void UpdateVisibleText()
        {
            // Clear all text elements first
            for (var i = 0; i < _text.Length; i++)
            {
                _text[i].Caption = string.Empty;
            }

            // Update with current visible lines
            var currentLines = _lines.ToArray();
            for (var i = 0; i < currentLines.Length && i < MaxVisibleLines; i++)
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
            if (line.Length > 40)
            {
                line = line.Substring(0, 37) + "...";
            }

            // Add to queue, removing old lines if we exceed MaxVisibleLines
            _lines.Enqueue(line);
            _lineTimes.Add(DateTime.Now);
            while (_lines.Count > MaxVisibleLines)
            {
                _lines.Dequeue();
                _lineTimes.RemoveAt(0);
            }

            // Update visible text elements
            UpdateVisibleText();

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
            _stayOnScreen = true;
            _backsplash.Color = Color.FromArgb(180, 0, 0, 0);
            foreach (var text in _text)
            {
                text.Color = Color.FromArgb(255, 255, 255, 255);
            }
            Logger.Debug("Console output shown");
        }

        public void Hide()
        {
            ThrowIfDisposed();
            Clear();  // Explicitly clear messages when hiding
            _stayOnScreen = false;
            Logger.Debug("Console output hidden and cleared");
        }

        public void EnableFadeOut()
        {
            ThrowIfDisposed();
            _stayOnScreen = false;
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
            if (_disposed) return;

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
            }

            _disposed = true;
        }

        ~FrontendOutput()
        {
            Dispose(false);
        }
    }
}