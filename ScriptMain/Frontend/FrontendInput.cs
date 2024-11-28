using System;
using System.ComponentModel;
using System.Drawing;
using GTA;
using GTA.Native;
using GTA.UI;

namespace TornadoScript.ScriptMain.Frontend
{
    public class FrontendInput : IDisposable
    {
        private const int CursorPulseSpeed = 300;
        private bool _cursorState;
        private bool _active;
        private string _str = "";
        private int _lastCursorPulse, _currentTextWidth;
        private bool _disposed;

        private readonly TextElement _text = new TextElement("", new Point(14, 5), 0.3f);
        private readonly Rectangle _cursorRect = new Rectangle(14, 5, 1, 15);
        private readonly IElement _backgroundContainer;

        public FrontendInput()
        {
            _backgroundContainer = new TextElement("", new Point(20, 20), 0.3f);
        }

        public void Update(int gameTime)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FrontendInput));
            if (!_active) return;

            if (gameTime > _lastCursorPulse + CursorPulseSpeed)
            {
                _cursorState = !_cursorState;
                _lastCursorPulse = gameTime;
            }

            _text.Draw();

            if (_cursorState)
            {
                Function.Call(Hash.DRAW_RECT, _cursorRect.X + _currentTextWidth, _cursorRect.Y,
                    _cursorRect.Width, _cursorRect.Height, Color.White.R, Color.White.G,
                    Color.White.B, Color.White.A);
            }
        }

        public void Show()
        {
            _active = true;
            _cursorState = true;
            _lastCursorPulse = Game.GameTime;
        }

        public void Hide()
        {
            _active = false;
        }

        public void AddChar(char c)
        {
            _str += c;
            UpdateText();
        }

        public void RemoveLastChar()
        {
            if (_str.Length > 0)
            {
                _str = _str.Substring(0, _str.Length - 1);
                UpdateText();
            }
        }

        public void Clear()
        {
            _str = "";
            UpdateText();
        }

        public string GetText()
        {
            return _str;
        }

        private void UpdateText()
        {
            _text.Caption = _str;
            _currentTextWidth = (int)(_str.Length * 6.5f); // Approximate width per character
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
                Hide();
                Clear();
                
                if (_text is IDisposable textDisposable)
                {
                    textDisposable.Dispose();
                }
                
                if (_backgroundContainer is IDisposable backgroundDisposable)
                {
                    backgroundDisposable.Dispose();
                }
            }

            // Clean up unmanaged resources (if any)
            _disposed = true;
        }

        ~FrontendInput()
        {
            Dispose(false);
        }
    }
}