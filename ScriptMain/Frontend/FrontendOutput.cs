using System;
using System.Drawing;
using GTA;
using GTA.UI;

namespace TornadoScript.ScriptMain.Frontend
{
    public class FrontendOutput : IDisposable
    {
        private const int TextActiveTime = 10000;
        private readonly TextElement[] _text = new TextElement[10];
        private readonly ContainerElement _backsplash = new ContainerElement();
        private readonly string[] _messageQueue = new string[20];
        private bool _startFromTop = true;
        private bool _stayOnScreen;
        private int _scrollIndex;
        private int _shownTime;
        private int _linesCount;
        private bool _disposed;

        public void Clear()
        {
            ThrowIfDisposed();
            Array.Clear(_messageQueue, 0, _messageQueue.Length);
            _linesCount = 0;
            _scrollIndex = 0;
        }

        public FrontendOutput()
        {
            _backsplash = new ContainerElement(new Point(20, 60), new Size(600, 200), Color.FromArgb(140, 52, 144, 2));
            CreateText();
        }

        private void CreateText()
        {
            for (var i = 0; i < _text.Length; i++)
            {
                _text[i] = new TextElement(string.Empty, new Point(14, 11 + 18 * i), 0.3f);
                _backsplash.Items.Add(_text[i]);
            }
        }

        public void Update(int gameTime)
        {
            ThrowIfDisposed();
            if (gameTime > _shownTime + TextActiveTime && !_stayOnScreen)
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
            else
            {
                UpdateText();
            }

            _backsplash.Draw();
        }

        private void UpdateText()
        {
            for (var i = _text.Length - 1; i > -1; i--)
            {
                _text[i].Caption = _messageQueue[
                    _startFromTop
                        ? i + _scrollIndex
                        : _messageQueue.Length - 1 - i + _scrollIndex] ?? string.Empty;
            }
        }

        public void WriteLine(string text)
        {
            ThrowIfDisposed();
            Show();

            for (var i = _messageQueue.Length - 1; i > 0; i--)
            {
                _messageQueue[i] = _messageQueue[i - 1];
            }

            _messageQueue[0] = $"~w~{text}";
            _linesCount = Math.Min(_linesCount + 1, _messageQueue.Length);
        }

        public void Show()
        {
            ThrowIfDisposed();
            _backsplash.Color = Color.FromArgb(140, 52, 144, 2);
            SetTextColor(Color.White);
            _shownTime = Game.GameTime;
            _stayOnScreen = true;
        }

        public void Hide()
        {
            ThrowIfDisposed();
            _stayOnScreen = false;
        }

        public void EnableFadeOut()
        {
            ThrowIfDisposed();
            _stayOnScreen = false;
        }

        public void ScrollUp()
        {
            ThrowIfDisposed();
            if (_scrollIndex > 0)
            {
                _scrollIndex--;
                UpdateText();
            }
        }

        public void ScrollDown()
        {
            ThrowIfDisposed();
            if (_scrollIndex < _linesCount - _text.Length)
            {
                _scrollIndex++;
                UpdateText();
            }
        }

        public void ScrollToTop()
        {
            ThrowIfDisposed();
            _scrollIndex = 0;
            UpdateText();
        }

        private void SetTextColor(Color color)
        {
            ThrowIfDisposed();
            foreach (var text in _text)
            {
                text.Color = color;
            }
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
            GC.SuppressFinalize(this);
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