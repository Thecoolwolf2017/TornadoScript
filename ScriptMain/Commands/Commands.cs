using GTA;
using GTA.Native;
using System.Text;
using TornadoScript.ScriptCore.Game;
using TornadoScript.ScriptMain.Script;

namespace TornadoScript.ScriptMain.Commands
{
    public static class Commands
    {
        public static string SetVar(params string[] args)
        {
            if (args.Length < 2) return "Set: Bad format";

            var varName = args[0];

            if (int.TryParse(args[1], out var _i))
            {
                if (ScriptThread.GetVar<int>(varName) != null)
                {
                    return !ScriptThread.SetVar(varName, _i)
                        ? "Set failed"
                        : null;
                }
            }

            if (float.TryParse(args[1], out var f))
            {
                var foundVar = ScriptThread.GetVar<float>(varName);

                if (foundVar != null)
                {
                    return !ScriptThread.SetVar(varName, f)
                        ? "Set failed"
                        : null;
                }
            }

            if (bool.TryParse(args[1], out var b))
            {
                var foundVar = ScriptThread.GetVar<bool>(varName);

                if (foundVar != null)
                {
                    return !ScriptThread.SetVar(varName, b)
                        ? "Set failed"
                        : null;
                }
            }

            return $"'{varName}' not found";
        }

        public static string ResetVar(params string[] args)
        {
            if (args.Length < 1) return "ResetVar: Invalid format.";

            var varName = args[0];

            var intVar = ScriptThread.GetVar<int>(varName);
            if (intVar != null)
            {
                intVar.Value = intVar.Default;
                return null;
            }

            var floatVar = ScriptThread.GetVar<float>(varName);
            if (floatVar != null)
            {
                floatVar.Value = floatVar.Default;
                return null;
            }

            var boolVar = ScriptThread.GetVar<bool>(varName);
            if (boolVar != null)
            {
                boolVar.Value = boolVar.Default;
                return null;
            }

            return "Variable '" + varName + "' not found.";
        }

        public static string ListVars(string[] _)
        {
            var vars = ScriptThread.Vars;
            var result = new StringBuilder();
            result.AppendLine("Vars:");

            // Only show first 6 variables to prevent overflow
            int shown = 0;
            foreach (var var in vars)
            {
                if (shown >= 6) break;
                // Shorten output by truncating values
                string value = var.Value.ToString();
                if (value.Length > 10)
                    value = value.Substring(0, 7) + "...";
                result.AppendLine($"{var.Key}={value}");
                shown++;
            }

            if (vars.Count > 6)
            {
                result.AppendLine("+more");
            }

            return result.ToString();
        }

        public static string SummonVortex(string[] _)
        {
            var vtxmgr = ScriptThread.Get<TornadoFactory>();
            if (vtxmgr.ActiveVortexCount > 0)
                vtxmgr.ActiveVortexList[0].Position = Game.Player.Character.Position;
            return "Moved";
        }

        public static string SpawnVortex(string[] _)
        {
            var vtxmgr = ScriptThread.Get<TornadoFactory>();
            Function.Call(Hash.REMOVE_PARTICLE_FX_IN_RANGE, 0f, 0f, 0f, 1000000.0f);
            Function.Call(Hash.SET_WIND, 70.0f);
            var position = Game.Player.Character.Position + Game.Player.Character.ForwardVector * 180f;
            var tornado = vtxmgr.CreateVortex(position);
            if (tornado == null)
            {
                return "Failed to spawn tornado";
            }
            return "Spawned successfully";
        }

        public static string ShowHelp(params string[] _)
        {
            return "sp:new\n" +
                   "su:move\n" +
                   "set:val\n" +
                   "rst:def\n" +
                   "ls:vars\n" +
                   "clr:txt";
        }
    }
}
