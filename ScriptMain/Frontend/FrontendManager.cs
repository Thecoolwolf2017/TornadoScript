using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.Linq;
using TornadoScript.ScriptCore;
using TornadoScript.ScriptCore.Game;

namespace TornadoScript.ScriptMain.Frontend
{
    public class FrontendManager : ScriptExtension
    {
        private readonly FrontendInput _input;
        private readonly FrontendOutput _output;
        private bool _showingConsole;
        private readonly HashSet<string> _outputLines;
        private bool _isUpdating = false;
        private readonly object _consoleLock = new object();

        // Custom key codes enum
        public enum KeyCode
        {
            None = 0,
            Back = 8,
            Tab = 9,
            Enter = 13,
            Pause = 19,
            CapsLock = 20,
            Escape = 27,
            Space = 32,
            PageUp = 33,
            PageDown = 34,
            End = 35,
            Home = 36,
            Left = 37,
            Up = 38,
            Right = 39,
            Down = 40,
            Insert = 45,
            Delete = 46,
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
            F1 = 112,
            F2 = 113,
            F3 = 114,
            F4 = 115,
            F5 = 116,
            F6 = 117,
            F7 = 118,
            F8 = 119,
            F9 = 120,
            F10 = 121,
            F11 = 122,
            F12 = 123,
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
        public class KeyEventArgs
        {
            public KeyCode KeyCode { get; set; }
            public bool Shift { get; set; }
            public bool Control { get; set; }
            public bool Alt { get; set; }

            public KeyEventArgs(KeyCode keyCode, bool shift = false, bool control = false, bool alt = false)
            {
                KeyCode = keyCode;
                Shift = shift;
                Control = control;
                Alt = alt;
            }
        }

        public FrontendManager()
        {
            Name = "Frontend";
            _input = new FrontendInput();
            _output = new FrontendOutput();
            _outputLines = new HashSet<string>();
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
            if (_showingConsole)
            {
                // Disable ALL game controls and actions
                Function.Call(Hash.DISABLE_ALL_CONTROL_ACTIONS, 0);
                Function.Call(Hash.DISABLE_ALL_CONTROL_ACTIONS, 1);
                Function.Call(Hash.DISABLE_ALL_CONTROL_ACTIONS, 2);

                // Comprehensive phone control disabling
                Game.DisableControlThisFrame(Control.Phone);
                Game.DisableControlThisFrame(Control.PhoneCancel);
                Game.DisableControlThisFrame(Control.PhoneUp);
                Game.DisableControlThisFrame(Control.PhoneDown);
                Game.DisableControlThisFrame(Control.PhoneLeft);
                Game.DisableControlThisFrame(Control.PhoneRight);
                Game.DisableControlThisFrame(Control.PhoneSelect);

                // Attempt to forcibly close any open phone
                // Removed problematic hash call

                // Disable frontend controls
                Game.DisableControlThisFrame(Control.SelectWeapon);
                Game.DisableControlThisFrame(Control.RadioWheelLeftRight);
                Game.DisableControlThisFrame(Control.RadioWheelUpDown);
                Game.DisableControlThisFrame(Control.VehicleRadioWheel);
                Game.DisableControlThisFrame(Control.FrontendPause);
                Game.DisableControlThisFrame(Control.FrontendPauseAlternate);
                Game.DisableControlThisFrame(Control.WeaponWheelLeftRight);
                Game.DisableControlThisFrame(Control.WeaponWheelUpDown);
                Game.DisableControlThisFrame(Control.CharacterWheel);

                // Update input and output
                _input?.Update(gameTime);
                _output?.Update(gameTime);
            }
        }

        public void OnKeyDown(KeyEventArgs e)
        {
            if (!_showingConsole) return;

            Logger.Debug($"Processing console key: {e.KeyCode}");

            // Handle special keys
            switch (e.KeyCode)
            {
                case KeyCode.Enter:
                    string command = _input.GetCurrentInput();
                    if (!string.IsNullOrWhiteSpace(command))
                    {
                        Logger.Info($"Console command entered: {command}");
                        _output.WriteLine("> " + command, false, true);  // Echo command with isCommandEcho=true
                        ProcessCommand(command);  // Process the command
                        _input.ClearInput();
                    }
                    break;

                case KeyCode.Back:
                    _input.Backspace();
                    break;

                case KeyCode.Escape:
                    Logger.Info("Console closed via Escape key");
                    HideConsole();
                    break;

                default:
                    // Handle regular character input
                    char? c = GetCharFromKey(e.KeyCode, e.Shift);
                    if (c.HasValue)
                    {
                        _input.AddChar(c.Value);
                        Logger.Debug($"Console key pressed: {e.KeyCode} (char: {c.Value})");
                    }
                    break;
            }
        }

        private void ProcessCommand(string command)
        {
            Logger.Debug($"Processing command: {command}");

            // Split command and arguments
            var parts = command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;

            var cmd = parts[0].ToLower();
            var args = parts.Skip(1).ToArray();

            try
            {
                string result = null;

                // Map commands to Commands class methods
                switch (cmd)
                {
                    case "set":
                        result = TornadoScript.ScriptMain.Commands.Commands.SetVar(args);
                        break;
                    case "reset":
                        result = TornadoScript.ScriptMain.Commands.Commands.ResetVar(args);
                        break;
                    case "ls":
                    case "list":
                        result = TornadoScript.ScriptMain.Commands.Commands.ListVars(args);
                        break;
                    case "spawn":
                        result = TornadoScript.ScriptMain.Commands.Commands.SpawnVortex(args);
                        break;
                    case "summon":
                        result = TornadoScript.ScriptMain.Commands.Commands.SummonVortex(args);
                        break;
                    case "help":
                    case "?":
                        result = TornadoScript.ScriptMain.Commands.Commands.ShowHelp(args);
                        break;
                    case "clear":
                        _output.Clear();
                        // Preserve help text after clear
                        _output.WriteLine("Console cleared. Type 'help' for available commands.", false, false);
                        break;
                    case "exit":
                        HideConsole();
                        break;
                    default:
                        _output.WriteLine($"Unknown command: {cmd}", false, false);
                        _output.WriteLine("Type 'help' for available commands.", false, false);
                        break;
                }

                // Output command result if any
                if (result != null)
                {
                    // Split multi-line results and output each line
                    var resultLines = result.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in resultLines)
                    {
                        var trimmedLine = line.Trim();
                        _output.WriteLine(trimmedLine, false, false);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error executing command '{command}': {ex.Message}");
                _output.WriteLine($"Error: {ex.Message}", false, false);
                _output.WriteLine("Type 'help' for available commands.", false, false);
            }
        }

        private char? GetCharFromKey(KeyCode key, bool shift)
        {
            // Handle letters
            if (key >= KeyCode.A && key <= KeyCode.Z)
            {
                char c = (char)('a' + (key - KeyCode.A));
                return shift ? char.ToUpper(c) : c;
            }

            // Handle numbers
            if (key >= KeyCode.D0 && key <= KeyCode.D9 && !shift)
            {
                return (char)('0' + (key - KeyCode.D0));
            }

            // Handle special characters
            return key switch
            {
                KeyCode.Space => ' ',
                KeyCode.OemMinus => shift ? '_' : '-',
                KeyCode.OemPlus => shift ? '+' : '=',
                KeyCode.OemPeriod => shift ? '>' : '.',
                KeyCode.OemComma => shift ? '<' : ',',
                KeyCode.OemQuestion => shift ? '?' : '/',
                KeyCode.OemSemicolon => shift ? ':' : ';',
                KeyCode.OemQuotes => shift ? '"' : '\'',
                KeyCode.OemOpenBrackets => shift ? '{' : '[',
                KeyCode.OemCloseBrackets => shift ? '}' : ']',
                KeyCode.OemPipe => shift ? '|' : '\\',
                KeyCode.OemTilde => shift ? '~' : '`',
                _ => null
            };
        }

        public void HandleKeyPress(System.Windows.Forms.KeyEventArgs e)
        {
            var keyArgs = new KeyEventArgs(
                (KeyCode)e.KeyCode,
                e.Shift,
                e.Control,
                e.Alt
            );
            OnKeyDown(keyArgs);
        }

        protected override void OnKeyDown(object sender, ScriptEventArgs args)
        {
            base.OnKeyDown(sender, args);

            // Extract KeyEventArgs from ScriptEventArgs.Data
            if (args.Data is KeyEventArgs keyArgs)
            {
                OnKeyDown(keyArgs);
            }
        }

        public void ShowConsole()
        {
            lock (_consoleLock)
            {
                if (!_isUpdating)
                {
                    _isUpdating = true;
                    _output?.Show();
                    _input?.Enable();
                    _showingConsole = true;
                    _isUpdating = false;
                }
            }
        }

        public void HideConsole()
        {
            lock (_consoleLock)
            {
                if (!_isUpdating)
                {
                    _isUpdating = true;
                    _output?.Hide();
                    _input?.Disable();
                    _showingConsole = false;
                    _isUpdating = false;
                }
            }
        }

        public void WriteLine(string format, params object[] args)
        {
            if (args == null || args.Length == 0)
                _output.WriteLine(format, false, false);
            else
                _output.WriteLine(string.Format(format, args), false, false);
        }

        public void Clear()
        {
            _output.Clear();
        }

        public void ResetOutputLines()
        {
            _outputLines?.Clear();
            _output?.Clear();
        }

        public bool IsConsoleShowing => _showingConsole;

        public override void Dispose()
        {
            _input?.Dispose();
            _output?.Dispose();
            base.Dispose();
        }
    }
}
