using GTA.Native;
using GTA.UI;
using System;
using System.Drawing;

namespace TornadoScript.ScriptMain.Frontend
{
    public class FrontendInput : IDisposable
    {
        private const int CursorPulseSpeed = 300;
        private const float TextScale = 0.3f;  // Match output text scale
        private const int ConsoleX = 20;  // Match output X position
        private const int InputY = 280;  // Aligned with output console position
        private const int TextX = ConsoleX + 15;  // Match output text indent
        private const int TextOffsetY = -20;  // Negative offset to move text up inside console
        private const int LineHeight = 25;  // Match output console line height
        private const int ConsoleWidth = 500;  // Match output console width
        private const int TextPadding = 2;  // Small padding for text

        private bool _cursorState;
        private bool _active;
        private string _str = "";
        private int _lastCursorPulse, _currentTextWidth;
        private bool _disposed;

        private readonly TextElement[] _textElements = new TextElement[1];
        private readonly Rectangle _cursorRect;
        private readonly IElement _backgroundContainer;

        public FrontendInput()
        {
            // Extensive logging for debugging
            TornadoScript.ScriptCore.Logger.Debug($"FrontendInput Debug:");
            TornadoScript.ScriptCore.Logger.Debug($"ConsoleX: {ConsoleX}");
            TornadoScript.ScriptCore.Logger.Debug($"InputY: {InputY}");
            TornadoScript.ScriptCore.Logger.Debug($"TextX: {TextX}");
            TornadoScript.ScriptCore.Logger.Debug($"ConsoleWidth: {ConsoleWidth}");
            TornadoScript.ScriptCore.Logger.Debug($"LineHeight: {LineHeight}");
            TornadoScript.ScriptCore.Logger.Debug($"TextPadding: {TextPadding}");

            // Initialize text element with consistent styling
            _textElements[0] = new TextElement(
                ">",
                new Point(TextX, InputY + TextPadding),
                TextScale,
                Color.FromArgb(255, 255, 255, 255),
                GTA.UI.Font.ChaletLondon,
                GTA.UI.Alignment.Left
            );

            // Align background with output
            _backgroundContainer = new ContainerElement(
                new Point(ConsoleX, InputY),
                new Size(ConsoleWidth, LineHeight),
                Color.FromArgb(180, 0, 0, 0)  // Match output transparency
            );

            // Align cursor with text
            _cursorRect = new Rectangle(
                TextX + 20,  // Adjusted for prompt character
                InputY + TextPadding,  // Small offset for text baseline
                1,          // Thin cursor
                16         // Height based on text scale
            );
        }

        public void Update(int gameTime)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FrontendInput));
            if (!_active) return;

            // Draw background first
            _backgroundContainer.Draw();

            if (gameTime > _lastCursorPulse + CursorPulseSpeed)
            {
                _cursorState = !_cursorState;
                _lastCursorPulse = gameTime;
            }

            // Draw text with prefix
            _textElements[0].Draw();

            if (_cursorState)
            {
                Function.Call(Hash.DRAW_RECT, _cursorRect.X + _currentTextWidth, _cursorRect.Y,
                    _cursorRect.Width, _cursorRect.Height, Color.White.R, Color.White.G,
                    Color.White.B, Color.White.A);
            }
        }

        public void Enable()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FrontendInput));
            _active = true;
            _str = "";  // Clear input on enable
            UpdateText();
        }

        public void Disable()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FrontendInput));
            _active = false;
            _str = "";  // Clear input on disable
            UpdateText();
        }

        public void AddChar(char c)
        {
            _str += c;
            UpdateText();
        }

        public void RemoveLastChar()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FrontendInput));
            if (_str.Length > 0)
            {
                _str = _str.Substring(0, _str.Length - 1);
                UpdateText();
            }
        }

        public void Backspace()
        {
            RemoveLastChar();
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
            if (_disposed) throw new ObjectDisposedException(nameof(FrontendInput));
            // Update the text element with current input
            _textElements[0].Caption = $"> {_str}";
            _currentTextWidth = (int)(_textElements[0].Scale * _textElements[0].Caption.Length * 6); // Approximate width
        }

        public string GetCurrentInput()
        {
            return _str;
        }

        public void ClearInput()
        {
            _str = "";
            UpdateText();
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
                Disable();
                Clear();

                if (_textElements[0] is IDisposable textDisposable)
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