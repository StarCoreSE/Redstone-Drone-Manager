using CoreSystems.Api;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRageMath;

namespace IngameScript
{
    internal class WCTargetingHelper : TargetingHelper
    {
        readonly WcPbApi wAPI = new WcPbApi();
        readonly bool canRun;

        public WCTargetingHelper(Program program) : base(program)
        {
            canRun = wAPI.Activate(program.Me);
            if (!canRun)
                program.Echo("WcPbAPI failed to init!");
            wepCheck = new Func<IMyTerminalBlock, bool>(w => wAPI.HasCoreWeapon(w));
        }

        public override void Update()
        {
            if (!canRun)
                return;
            base.Update();

            target = wAPI.GetAiFocus(program.gridId);
        }

        public override void GetTargets()
        {
            Dictionary<MyDetectedEntityInfo, float> t = new Dictionary<MyDetectedEntityInfo, float>();
            wAPI.GetSortedThreats(program.Me, t);
            targets = t.Keys.ToArray();
        }

        public override void SetTarget(MyDetectedEntityInfo target)
        {
            base.SetTarget(target);
            wAPI.SetAiFocus(program.Me, target.EntityId);
        }

        public override void FireWeapon(IMyTerminalBlock weapon)
        {
            wAPI.FireWeaponOnce(weapon);
        }
        public override void FireWeapon(IMyTerminalBlock weapon, bool enabled)
        {
            if (enabled && wAPI.IsWeaponReadyToFire(weapon))
                if (!weapon.GetValueBool("WC_Shoot"))
                    weapon.SetValueBool("WC_Shoot", true);
            else if (weapon.GetValueBool("WC_Shoot"))
                //wAPI.ToggleWeaponFire(weapon, false, true);
                weapon.SetValueBool("WC_Shoot", false);
        }

        public override float MaxRange(IMyTerminalBlock weapon)
        {
            return wAPI.GetMaxWeaponRange(weapon, 0);
        }

        public override bool WeaponReady(IMyTerminalBlock weapon)
        {
            return wAPI.IsWeaponReadyToFire(weapon);
        }
    }
}
