using GTA.Math;
using System;

namespace TornadoScript.ScriptCore.Game
{
    public interface IScriptComponent : IScriptUpdatable
    {
        string Name { get; }
        Guid Guid { get; }
        Vector3 GetPosition();
    }
}