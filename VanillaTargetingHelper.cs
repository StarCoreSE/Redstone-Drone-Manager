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

        public override MyDetectedEntityInfo? GetClosestTarget()
        {
            throw new Exception();
        }
    }
}
