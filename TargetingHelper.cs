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
        public MyDetectedEntityInfo Target { get; protected set; }
        protected Func<IMyTerminalBlock, bool> wepCheck;
        protected double targetDistance = 0;
        protected Vector3D centerOfGrid = Vector3D.Zero;
        public MyDetectedEntityInfo[] Targets = new MyDetectedEntityInfo[0];


        public static bool WcPbApiExists(IMyProgrammableBlock Me)
        {
            return new WcPbApi().Activate(Me);
        }

        public TargetingHelper(Program program) {
            this.program = program;
        }

        public virtual void Update()
        {
            centerOfGrid = program.Me.CubeGrid.GetPosition();
            targetDistance = Vector3D.Distance(centerOfGrid, Target.Position);
            GetTargets();
        }

        public virtual void SetTarget(MyDetectedEntityInfo target)
        {
            Target = target;
        }
        public virtual void SetTarget(long targetId)
        {
            foreach (var target in Targets)
                if (target.EntityId == targetId)
                    SetTarget(target);
        }

        public virtual long GetClosestTarget()
        {
            long target = 0;

            foreach (var targ in Targets) // goofy ahh distance sorter
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
        

        public virtual List<T> GetWeapons<T>(List<T> weapons) where T : class, IMyTerminalBlock
        {
            program.GridTerminalSystem.GetBlocksOfType(weapons, w => wepCheck(w));

            return weapons;
        }

        public virtual List<T> GetWeapons<T>(List<T> weapons, string group) where T : class, IMyTerminalBlock
        {
            program.GridTerminalSystem.GetBlockGroupWithName(group).GetBlocksOfType(weapons, w => wepCheck(w));

            return weapons;
        }

        public virtual List<T> GetWeapons<T>(List<T> weapons, Func<IMyTerminalBlock, bool> collect) where T : class, IMyTerminalBlock
        {
            program.GridTerminalSystem.GetBlocksOfType(weapons, w => collect(w) && wepCheck(w));

            return weapons;
        }

        public abstract void GetTargets();
        public abstract void FireWeapon(IMyTerminalBlock weapon, bool enabled);
        public abstract void FireWeapon(IMyTerminalBlock weapon);
        public abstract float GetMaxRange(IMyTerminalBlock weapon);
        public abstract bool GetWeaponReady(IMyTerminalBlock weapon);
        public abstract void GetObstructions(List<MyDetectedEntityInfo> obstructions);
        public abstract Vector3D? GetPredictedPosition(IMyTerminalBlock weapon, long entityId);
    }
}
