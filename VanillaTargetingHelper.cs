using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRageMath;

namespace IngameScript
{
    internal class VanillaTargetingHelper : TargetingHelper
    {
        List<IMyLargeTurretBase> turrets = new List<IMyLargeTurretBase>();
        List<IMyLargeTurretBase> tcbs = new List<IMyLargeTurretBase>();

        public VanillaTargetingHelper(Program program) : base(program)
        {
            wepCheck = new Func<IMyTerminalBlock, bool>(w => w is IMyUserControllableGun || w is IMyTurretControlBlock);
        }

        public override void Update()
        {
            program.outText += "VANILLA!!!";

            updateCount++;
            if (updateCount == 100)
                Update100();
        }

        int updateCount = 0;
        public void Update100()
        {
            GetWeapons(turrets);
            GetWeapons(tcbs);

            updateCount = 0;
        }

        public override void GetTargets()
        {
            List<MyDetectedEntityInfo> _targets = new List<MyDetectedEntityInfo>();
            foreach (var turret in turrets)
                _targets.Add(turret.GetTargetedEntity());
            foreach (var tcb in tcbs)
                _targets.Add(tcb.GetTargetedEntity());

            Targets = _targets.ToArray();
        }

        public override void FireWeapon(IMyTerminalBlock weapon, bool enabled)
        {
            if (weapon is IMyUserControllableGun)
                ((IMyUserControllableGun) weapon).Shoot = enabled;
        }

        public override void FireWeapon(IMyTerminalBlock weapon)
        {
            if (weapon is IMyUserControllableGun)
                ((IMyUserControllableGun)weapon).ShootOnce();
        }

        public override float GetMaxRange(IMyTerminalBlock weapon)
        {
            if (weapon is IMyLargeTurretBase)
                return ((IMyLargeTurretBase)weapon).Range;
            if (weapon is IMyTurretControlBlock)
                return ((IMyTurretControlBlock)weapon).Range;
            return 0;
        }

        public override bool GetWeaponReady(IMyTerminalBlock weapon)
        {
            return true;
        }

        public override void GetObstructions(List<MyDetectedEntityInfo> obstructions)
        {
            return;
        }

        public override Vector3D? GetPredictedPosition(IMyTerminalBlock weapon, long entityId)
        {
            throw new Exception("Not Implemented!");
        }
    }
}
