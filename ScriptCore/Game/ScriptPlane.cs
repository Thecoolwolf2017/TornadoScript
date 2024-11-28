using GTA;
using GTA.Math;
using GTA.Native;

namespace TornadoScript.ScriptCore.Game
{
    /// <summary>
    /// Represents a plane.
    /// </summary>
    public class ScriptPlane : ScriptEntity<Vehicle>, IScriptEntity
    {
        /// <summary>
        /// Fired when the vehicle is no longer drivable.
        /// </summary>
        public event ScriptEntityEventHandler Undrivable;
        public new Vehicle Ref { get; private set; }

        /// <summary>
        /// State of the vehicle landing gear.
        /// </summary>
        public LandingGearState LandingGearState
        {
            get { return (LandingGearState)Function.Call<int>(Hash.GET_LANDING_GEAR_STATE, Ref.Handle); }
            set { Function.Call(Hash.CONTROL_LANDING_GEAR, Ref.Handle, (int)value); }
        }

        private int undrivableTicks = 0;

        protected override Entity CreateEntity(Vector3 position)
        {
            return World.CreateVehicle(VehicleHash.Lazer, position);
        }

        protected virtual void OnUndrivable(ScriptEntityEventArgs e)
        {
            Undrivable?.Invoke(this, e);
        }

        public override void OnUpdate(int gameTime)
        {
            if (!Ref.IsDriveable)
            {
                if (undrivableTicks == 0)
                    OnUndrivable(new ScriptEntityEventArgs(gameTime));

                undrivableTicks++;
            }

            else
            {
                undrivableTicks = 0;
            }

            base.OnUpdate(gameTime);
        }
    }

    public enum LandingGearState
    {
        Deployed,
        Closing,
        Opening,
        Retracted
    }
}
