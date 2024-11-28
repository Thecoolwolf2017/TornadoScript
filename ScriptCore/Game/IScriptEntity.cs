using GTA;
using GTA.Math;

namespace TornadoScript.ScriptCore.Game
{
    public interface IScriptEntity : IScriptComponent
    {
        Entity Entity { get; }
        bool IsValid { get; }
        bool IsAlive { get; }
        void Destroy();
        void Teleport(Vector3 position);
        void SetInvincible(bool invincible);
    }
}
