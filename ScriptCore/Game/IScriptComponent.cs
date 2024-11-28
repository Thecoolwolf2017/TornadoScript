using System;
using GTA.Math;

namespace TornadoScript.ScriptCore.Game
{
    public interface IScriptComponent : IScriptUpdatable
    {
        string Name { get; }
        Guid Guid { get; }
        Vector3 GetPosition();
    }
}