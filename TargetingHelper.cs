using Sandbox.Game.GameSystems;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox.ModAPI.Ingame;
using CoreSystems.Api;
using VRageMath;

namespace IngameScript
{
    internal abstract class TargetingHelper
    {
        protected Program program;
        public MyDetectedEntityInfo? target { get; protected set; }
        protected Func<IMyTerminalBlock, bool> wepCheck;
        protected double targetDistance = 0;
        protected Vector3D centerOfGrid = Vector3D.Zero;
        public MyDetectedEntityInfo[] targets = new MyDetectedEntityInfo[0];


        public TargetingHelper(Program program) {
            this.program = program;
        }

        public virtual void Update()
        {
            centerOfGrid = program.Me.CubeGrid.GetPosition();
            if (target.HasValue)
                targetDistance = Vector3D.Distance(centerOfGrid, target.Value.Position);
            GetTargets();
        }

        public virtual void SetTarget(MyDetectedEntityInfo target)
        {
            this.target = target;
        }

        public virtual long GetClosestTarget()
        {
            long target = 0;

            foreach (var targ in targets) // goofy ahh distance sorter
            {
                double dist2 = Vector3D.DistanceSquared(targ.Position, centerOfGrid);
                if (dist2 < targetDistance || targetDistance == 0)
                {
                    targetDistance = dist2;
                    target = targ.EntityId;
                }
            }

            return target;
        }
        

        public virtual List<T> GetWeapons<T>() where T : class, IMyTerminalBlock
        {
            List<T> weapons = new List<T>();

            program.GridTerminalSystem.GetBlocksOfType(weapons, w => wepCheck(w));

            return weapons;
        }

        public virtual List<T> GetWeapons<T>(Func<IMyTerminalBlock, bool> collect) where T : class, IMyTerminalBlock
        {
            List<T> weapons = new List<T>();

            program.GridTerminalSystem.GetBlocksOfType(weapons, w => collect(w) && wepCheck(w));

            return weapons;
        }

        public abstract void GetTargets();
        public abstract void FireWeapon(IMyTerminalBlock weapon, bool enabled);
        public abstract void FireWeapon(IMyTerminalBlock weapon);
        public abstract float MaxRange(IMyTerminalBlock weapon);
        public abstract bool WeaponReady(IMyTerminalBlock weapon);
    }
}
