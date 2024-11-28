using GTA;
using GTA.Math;
using TornadoScript.ScriptCore.Game;
using TornadoScript.ScriptMain.Utility;

namespace TornadoScript.ScriptMain.Frontend
{
    public class FrontendManager : ScriptExtension
    {
        private readonly FrontendInput _input;
        private readonly FrontendOutput _output;
        private bool _showingConsole;
        private bool _capsLock;

        // Define our own key codes to remove Windows.Forms dependency
        private enum KeyCode
        {
            Back = 8,
            Tab = 9,
            Enter = 13,
            Shift = 16,
            Control = 17,
            Alt = 18,
            CapsLock = 20,
            Escape = 27,
            Space = 32,
            Left = 37,
            Up = 38,
            Right = 39,
            Down = 40,
            D0 = 48,
            D1 = 49,
            D2 = 50,
            D3 = 51,
            D4 = 52,
            D5 = 53,
            D6 = 54,
            D7 = 55,
            D8 = 56,
            D9 = 57,
            A = 65,
            B = 66,
            C = 67,
            D = 68,
            E = 69,
            F = 70,
            G = 71,
            H = 72,
            I = 73,
            J = 74,
            K = 75,
            L = 76,
            M = 77,
            N = 78,
            O = 79,
            P = 80,
            Q = 81,
            R = 82,
            S = 83,
            T = 84,
            U = 85,
            V = 86,
            W = 87,
            X = 88,
            Y = 89,
            Z = 90,
            NumPad0 = 96,
            NumPad1 = 97,
            NumPad2 = 98,
            NumPad3 = 99,
            NumPad4 = 100,
            NumPad5 = 101,
            NumPad6 = 102,
            NumPad7 = 103,
            NumPad8 = 104,
            NumPad9 = 105,
            OemMinus = 189,
            OemPlus = 187,
            OemOpenBrackets = 219,
            OemCloseBrackets = 221,
            OemPipe = 220,
            OemSemicolon = 186,
            OemQuotes = 222,
            OemComma = 188,
            OemPeriod = 190,
            OemQuestion = 191,
            OemTilde = 192
        }

        // Custom key event args
        private class KeyEventArgs
        {
            public KeyCode KeyCode { get; }
            public bool Shift { get; }
            public bool Control { get; }
            public bool Alt { get; }

            public KeyEventArgs(KeyCode keyCode, bool shift, bool control, bool alt)
            {
                KeyCode = keyCode;
                Shift = shift;
                Control = control;
                Alt = alt;
            }
        }

        private static readonly char[] SpecialChars = {
            ')', '!', '@', '#', '$', '%', '^', '&', '*', '(',
            '_', '+', '{', '}', '|', ':', '"', '<', '>', '?',
            '~'
        };

        private static readonly char[] NumberChars = {
            ')', '!', '@', '#', '$', '%', '^', '&', '*', '('
        };

        public FrontendManager()
        {
            Name = "Frontend";
            _input = new FrontendInput();
            _output = new FrontendOutput();
            RegisterEvent("keydown");
            RegisterEvent("textadded");
        }

        public override void OnThreadAttached()
        {
            base.OnThreadAttached();
        }

        public override void OnThreadDetached()
        {
            base.OnThreadDetached();
        }

        public override Vector3 GetPosition()
        {
            // Frontend is always relative to screen space, not world space
            return Vector3.Zero;
        }

        public override void OnUpdate(int gameTime)
        {
            _input.Update(gameTime);
            _output.Update(gameTime);

            if (_showingConsole)
            {
                if (Game.IsControlPressed(Control.FrontendUp))
                {
                    _output.ScrollUp();
                }
                else if (Game.IsControlPressed(Control.FrontendDown))
                {
                    _output.ScrollDown();
                }
            }

            base.OnUpdate(gameTime);
        }

        protected override void OnKeyDown(object sender, ScriptEventArgs args)
        {
            var keyArgs = args.Data as KeyEventArgs;
            if (keyArgs == null) return;

            if (!GetVar<bool>("enableconsole")) return;

            if (keyArgs.KeyCode == KeyCode.CapsLock)
            {
                _capsLock = !_capsLock;
                return;
            }

            if (!_showingConsole)
            {
                if (keyArgs.KeyCode == (KeyCode)GetVar<int>("toggleconsole"))
                {
                    ShowConsole();
                }
            }
            else
            {
                GetConsoleInput(keyArgs);
            }
        }

        private void GetConsoleInput(KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case KeyCode.Back:
                    {
                        var text = _input.GetText();
                        if (text.Length < 1)
                        {
                            HideConsole();
                        }
                        _input.RemoveLastChar();
                        return;
                    }

                case KeyCode.Up:
                    _output.ScrollUp();
                    return;

                case KeyCode.Down:
                    _output.ScrollDown();
                    return;

                case KeyCode.Space:
                    _input.AddChar(' ');
                    return;

                case KeyCode.Enter:
                    {
                        var text = _input.GetText();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            NotifyEvent("textadded", new ScriptEventArgs(text));
                            _output.WriteLine(text);
                            _input.Clear();
                            _output.ScrollToTop();
                        }
                        return;
                    }

                case KeyCode.Escape:
                    HideConsole();
                    return;
            }

            // Handle letters
            if (e.KeyCode >= KeyCode.A && e.KeyCode <= KeyCode.Z)
            {
                var keyChar = (char)e.KeyCode;
                
                if (_capsLock || e.Shift)
                    keyChar = char.ToUpper(keyChar);
                else
                    keyChar = char.ToLower(keyChar);

                _input.AddChar(keyChar);
                return;
            }

            // Handle numbers
            if ((e.KeyCode >= KeyCode.D0 && e.KeyCode <= KeyCode.D9) || 
                (e.KeyCode >= KeyCode.NumPad0 && e.KeyCode <= KeyCode.NumPad9))
            {
                var isNumPad = e.KeyCode >= KeyCode.NumPad0;
                var num = isNumPad ? (e.KeyCode - KeyCode.NumPad0) : (e.KeyCode - KeyCode.D0);
                
                if (e.Shift && !isNumPad)
                    _input.AddChar(NumberChars[num]);
                else
                    _input.AddChar((char)('0' + num));
                return;
            }

            // Handle special characters
            switch (e.KeyCode)
            {
                case KeyCode.OemMinus when e.Shift:
                    _input.AddChar('_');
                    break;
                case KeyCode.OemMinus:
                    _input.AddChar('-');
                    break;
                case KeyCode.OemPlus when e.Shift:
                    _input.AddChar('+');
                    break;
                case KeyCode.OemPlus:
                    _input.AddChar('=');
                    break;
                case KeyCode.OemOpenBrackets when e.Shift:
                    _input.AddChar('{');
                    break;
                case KeyCode.OemOpenBrackets:
                    _input.AddChar('[');
                    break;
                case KeyCode.OemCloseBrackets when e.Shift:
                    _input.AddChar('}');
                    break;
                case KeyCode.OemCloseBrackets:
                    _input.AddChar(']');
                    break;
                case KeyCode.OemPipe when e.Shift:
                    _input.AddChar('|');
                    break;
                case KeyCode.OemPipe:
                    _input.AddChar('\\');
                    break;
                case KeyCode.OemSemicolon when e.Shift:
                    _input.AddChar(':');
                    break;
                case KeyCode.OemSemicolon:
                    _input.AddChar(';');
                    break;
                case KeyCode.OemQuotes when e.Shift:
                    _input.AddChar('"');
                    break;
                case KeyCode.OemQuotes:
                    _input.AddChar('\'');
                    break;
                case KeyCode.OemComma when e.Shift:
                    _input.AddChar('<');
                    break;
                case KeyCode.OemComma:
                    _input.AddChar(',');
                    break;
                case KeyCode.OemPeriod when e.Shift:
                    _input.AddChar('>');
                    break;
                case KeyCode.OemPeriod:
                    _input.AddChar('.');
                    break;
                case KeyCode.OemQuestion when e.Shift:
                    _input.AddChar('?');
                    break;
                case KeyCode.OemQuestion:
                    _input.AddChar('/');
                    break;
                case KeyCode.OemTilde when e.Shift:
                    _input.AddChar('~');
                    break;
                case KeyCode.OemTilde:
                    _input.AddChar('`');
                    break;
            }
        }

        public void ShowConsole()
        {
            if (_showingConsole) return;
            _input.Show();
            _output.Show();
            _showingConsole = true;
        }

        public void HideConsole()
        {
            if (!_showingConsole) return;
            _input.Clear();
            _input.Hide();
            _output.Hide();
            _output.EnableFadeOut();
            _showingConsole = false;
        }

        public void WriteLine(string format, params object[] args)
        {
            if (args == null || args.Length == 0)
                _output.WriteLine(format);
            else
                _output.WriteLine(string.Format(format, args));
        }

        public void Clear()
        {
            _output.Clear();
        }

        public override void Dispose()
        {
            _input?.Dispose();
            _output?.Dispose();
            base.Dispose();
        }
    }
}
