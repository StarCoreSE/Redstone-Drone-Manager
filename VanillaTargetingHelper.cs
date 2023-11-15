using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IngameScript
{
    internal class VanillaTargetingHelper : TargetingHelper
    {
        public VanillaTargetingHelper(Program program) : base(program)
        {
            wepCheck = new Func<IMyTerminalBlock, bool>(w => w is IMyUserControllableGun);
        }

        public override void Update()
        {
            program.Echo("VANILLA!!!");
        }

        public override long GetClosestTarget()
        {
            throw new Exception();
        }

        public override void GetTargets()
        {
            throw new Exception();
        }

        public override void FireWeapon(IMyTerminalBlock weapon, bool enabled)
        {
            throw new Exception();
        }

        public override void FireWeapon(IMyTerminalBlock weapon)
        {
            throw new Exception();
        }

        public override float MaxRange(IMyTerminalBlock weapon)
        {
            throw new Exception();
        }

        public override bool WeaponReady(IMyTerminalBlock weapon)
        {
            throw new Exception();
        }
    }
}
