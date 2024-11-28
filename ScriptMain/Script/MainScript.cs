using System;
using GTA;
using GTA.Native;
using TornadoScript.ScriptCore.Game;
using TornadoScript.ScriptMain.Frontend;

namespace TornadoScript.ScriptMain.Script
{
    public class MainScript : ScriptThread, IDisposable
    {
        private TornadoFactory _factory;

        public MainScript()
        {
            RegisterVars();
            SetupAssets();
            _factory = GetOrCreate<TornadoFactory>();
            GetOrCreate<FrontendManager>();
            KeyDown += OnKeyDown;
        }

        private void RegisterVars()
        {
            RegisterVar("enableconsole", true);
            RegisterVar("toggleconsole", System.Windows.Forms.Keys.OemTilde);
        }

        private void SetupAssets()
        {
            // Initialize any required assets here
        }

        private static void ReleaseAssets()
        {
            // Cleanup any loaded assets here
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            NotifyEvent("keydown", new ScriptEventArgs(e));
        }

        void IDisposable.Dispose()
        {
            Function.Call(Hash.REMOVE_PARTICLE_FX_IN_RANGE, 0f, 0f, 0f, 1000000.0f);
            ReleaseAssets();
            base.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                KeyDown -= OnKeyDown;
            }
            base.Dispose(disposing);
        }
    }
}
