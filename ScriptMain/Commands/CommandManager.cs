using System;
using System.Collections.Generic;
using TornadoScript.ScriptCore;
using TornadoScript.ScriptCore.Game;
using TornadoScript.ScriptMain.Frontend;

namespace TornadoScript.ScriptMain.Commands
{
    public class CommandManager : ScriptExtension
    {
        public interface ICommandHandler
        {
            string Execute(string[] args);
        }
        private readonly Dictionary<string, ICommandHandler> _commands = new Dictionary<string, ICommandHandler>();
        private readonly FrontendManager _frontendMgr;

        public CommandManager()
        {
            try
            {
                Name = "Commands";
                Logger.Log("Initializing CommandManager...");

                // Get or create FrontendManager
                _frontendMgr = ScriptThread.Get<FrontendManager>();
                if (_frontendMgr == null)
                {
                    throw new InvalidOperationException("FrontendManager must be initialized before CommandManager");
                }

                RegisterEvent("textadded");
                Logger.Log("CommandManager events registered");

                InitializeCommands();
                Logger.Log("CommandManager commands initialized");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in CommandManager constructor: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private void InitializeCommands()
        {
            AddCommand("spawn", Commands.SpawnVortex);
            AddCommand("summon", Commands.SummonVortex);
            AddCommand("set", Commands.SetVar);
            AddCommand("reset", Commands.ResetVar);
            AddCommand("ls", Commands.ListVars);
            AddCommand("list", Commands.ListVars);
            AddCommand("help", Commands.ShowHelp);
            AddCommand("?", Commands.ShowHelp);
            AddCommand("clear", args => { _frontendMgr.Clear(); return null; });
            AddCommand("exit", args => { _frontendMgr.HideConsole(); return null; });
        }




        public override void OnThreadAttached()
        {

            Events["textadded"] += OnTextAdded;
            base.OnThreadAttached();
        }

        public override void OnThreadDetached()
        {
            Events["textadded"] -= OnTextAdded;
            base.OnThreadDetached();
        }


        private void OnTextAdded(object sender, ScriptEventArgs args)
        {
            if (args?.Data is string text && !string.IsNullOrEmpty(text))
            {
                try
                {
                    HandleCommand(text);
                }
                catch (Exception ex)
                {
                    _frontendMgr.WriteLine($"Error executing command: {ex.Message}");
                }
            }
        }

        private void HandleCommand(string command)
        {
            var args = command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (args.Length == 0) return;

            var commandName = args[0].ToLower();

            if (_commands.TryGetValue(commandName, out var handler))
            {
                var result = handler.Execute(args);
                if (!string.IsNullOrEmpty(result))
                {
                    _frontendMgr.WriteLine(result);
                }
            }
            else
            {
                _frontendMgr.WriteLine($"Unknown command: {commandName}. Type 'help' for a list of commands.");
            }
        }

        private void AddCommand(string name, Func<string[], string> command)
        {
            _commands[name.ToLower()] = new CommandHandler(command);
        }

        private class CommandHandler : ICommandHandler
        {
            private readonly Func<string[], string> _command;

            public CommandHandler(Func<string[], string> command)
            {
                _command = command ?? throw new ArgumentNullException(nameof(command));
            }

            public string Execute(string[] args)
            {
                return _command(args);
            }
        }
    }
}