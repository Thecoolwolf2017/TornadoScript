﻿using GTA;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TornadoScript.ScriptCore;
using TornadoScript.ScriptCore.Game;
using TornadoScript.ScriptMain.Frontend;
using TornadoScript.ScriptMain.Script;

namespace TornadoScript.ScriptMain.Commands
{
    public static class Commands
    {
        public static string SetVar(params string[] args)
        {
            if (args.Length < 2) return "SetVar: Invalid format.";

            var varName = args[0];


            if (int.TryParse(args[1], out var _i))
            {
                var foundVar = ScriptThread.GetVar<int>(varName);
                if (foundVar != null)
                {
                    return !ScriptThread.SetVar(varName, _i)
                        ? "Failed to set the (integer) variable. Is it readonly?"
                        : null;
                }
            }


            if (float.TryParse(args[1], out var f))
            {
                var foundVar = ScriptThread.GetVar<float>(varName);

                if (foundVar != null)
                {
                    return !ScriptThread.SetVar(varName, f)
                        ? "Failed to set the (float) variable. Is it readonly?"
                        : null;
                }
            }

            if (bool.TryParse(args[1], out var b))
            {
                var foundVar = ScriptThread.GetVar<bool>(varName);

                if (foundVar != null)
                {
                    return !ScriptThread.SetVar(varName, b)
                        ? "Failed to set the (bool) variable. Is it readonly?"
                        : null;
                }
            }

            return "Variable '" + args[0] + "' not found.";
        }

        public static string ResetVar(params string[] args)
        {
            if (args.Length < 1) return "ResetVar: Invalid format.";

            var varName = args[0];
            var intVar = ScriptThread.GetVar<int>(varName);
            var _ = ScriptThread.SetVar<float>(varName, intVar);
            if (intVar != null)
            {
                intVar.Value = intVar.Default;
                return null;
            }
            if (int.TryParse(args[1], out var i))
            {
                var foundVar = ScriptThread.GetVar<int>(varName);

                if (foundVar != null)
                {
                    foundVar.Value = foundVar.Default;

                    return null;
                }
            }


            if (float.TryParse(args[1], out var f))
            {
                var foundVar = ScriptThread.GetVar<float>(varName);

                if (foundVar != null)
                {
                    foundVar.Value = foundVar.Default;

                    return null;
                }
            }

            if (bool.TryParse(args[1], out var b))
            {
                var foundVar = ScriptThread.GetVar<bool>(varName);

                if (foundVar == null) return "Variable '" + args[0] + "' not found.";

                foundVar.Value = foundVar.Default;

                return null;
            }

            return "Variable '" + args[0] + "' not found.";
        }

        public static string ListVars(string[] _)
        {
            var vars = ScriptThread.Vars;
            var result = new StringBuilder();
            result.AppendLine("Available variables:");

            foreach (var var in vars)
            {
                result.AppendLine($"{var.Key} = {var.Value}");
            }

            return result.ToString();
        }

        public static string SummonVortex(string[] _)
        {
            var vtxmgr = ScriptThread.Get<TornadoFactory>();
            if (vtxmgr.ActiveVortexCount > 0)
                vtxmgr.ActiveVortexList[0].Position = Game.Player.Character.Position;
            return "Vortex summoned";
        }

        public static string SpawnVortex(string[] _)
        {
            var vtxmgr = ScriptThread.Get<TornadoFactory>();
            Function.Call(Hash.REMOVE_PARTICLE_FX_IN_RANGE, 0f, 0f, 0f, 1000000.0f);
            Function.Call(Hash.SET_WIND, 70.0f);
            var position = Game.Player.Character.Position + Game.Player.Character.ForwardVector * 180f;
            vtxmgr.CreateVortex(position);
            return "Vortex spawned";
        }

        public static string ShowHelp(params string[] _)
        {
            return "Available commands:\n" +
                   "spawn - Create a new tornado\n" +
                   "summon - Move existing tornado to player\n" +
                   "set <var> <value> - Set variable value\n" +
                   "reset <var> - Reset variable to default\n" +
                   "ls/list - List all variables\n" +
                   "help/? - Show this help";
        }
    }
}
