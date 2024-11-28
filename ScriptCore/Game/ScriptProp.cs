using GTA;
using GTA.Math;

namespace TornadoScript.ScriptCore.Game
{
    /// <summary>
    /// Represents a prop.
    /// </summary>
    public class ScriptProp : ScriptEntity<Prop>
    {
        protected override Entity CreateEntity(Vector3 position)
        {
            return World.CreateProp(new Model("prop_asteroid_01"), position, false, false);
        }
    }
}
