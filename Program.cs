using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        #region mdk preserve
        /* CONFIG */

        /* GENERAL SETTINGS */

        // If this PB is on a drone, set to 'false'. If this PB is on a ship, set to 'true'.
        bool isController = false;

        // Set this to true if multiple controllers are in use
        bool multipleControllers = false;

        // If speed <12 m/s, fortify and shieldfit 0. Else unfortify. Default [TRUE]
        bool autoFortify = true;

        // If enemy <100m, enable structural integrity. Else unset integrity. Default [TRUE]
        bool autoIntegrity = true;

        // If true, find own target. If false, use controller target. Default [FALSE]
        bool autoTarget = false;

        // If true, rotate around controller grid. If false, remain fixed. Only applies to controller. Default [TRUE]
        bool rotate = false;

        // Speed of rotation around parent grid. Higher = slower. Default [6]
        float rotateDividend = 6;

        // How far drones in formation should be from the controller.
        static int formDistance = 250;

        // How far drones in 'main' mode should orbit from the target.
        int mainDistance = 1000;

        // Toggles debug mode. Outputs performance, may increase performance cost
        bool debug = true;


        /* DRONE SETTINGS */

        // Set this to the grid's mass (in KG) IF there is no controller (cockpit, remote control) on the grid.
        float mass = 1300000;

        // Toggles if CCRP (autofire fixed guns) runs. Leave this on, I beg you. Default [TRUE]
        bool doCcrp = true;

        // Maximum innacuracy of CCRP in degrees. A lower number = higher accuracy requirement. Default [1]
        double maxOffset = 2;

        // Name of the terminal group containing the drone's fixed guns.
        string gunGroup = "Main";

        // Name of the terminal group containing the drone's afterburners. Make sure it's balanced!
        string abGroup = "Afterburners";

        // Radius of the harm zone.
        int zoneRadius = 12000;


        // PID values
        #region PID values
        static double kP = 32;
        static double kI = 0;
        static double kD = 32;
        static double lowerBound = -1000;
        static double upperBound = 1000;
        static double timeStep = 1.0 / 60;
        #endregion











        // DON'T EDIT BELOW THIS LINE UNLESS YOU REALLY KNOW WHAT YOU'RE DOING //
        // OR YOU'RE ARISTEAS //
        // I CAN'T STOP MYSELF //





















        #endregion

        // In Development Version //




















        int mode = 1;
        /*
         * 0 - Shoot and Scoot
         *     Swarm and Shoot
         * 
         * 1 - Wingman
         *     Orbit controller & fire at enemies
         * 
         * 2 - Fortify Fire
         *     GOTO controller and stay. Enter fortify and fire at enemies
         */

        char runIndicator = '|';

        PbApiWrapper dAPI;
        WcPbApi wAPI;
        DebugAPI d;

        bool canRun = false; // is weaponcore activated?
        bool activated = false; // have I been told to move?

        Vector3D centerOfGrid = new Vector3D(); // Me.position
        IMyCockpit cockpit;
        List<IMyTerminalBlock> detectors = new List<IMyTerminalBlock>();

        long frame = 0;

        MyDetectedEntityInfo aiTarget = new MyDetectedEntityInfo();

        GyroControl gyros;

        IMyBroadcastListener myBroadcastListener;
        IMyBroadcastListener positionListener;
        IMyBroadcastListener velocityListener;
        IMyBroadcastListener orientListener;
        IMyBroadcastListener performanceListener;
        IMyBroadcastListener dronePosListener;


        int group = 1; // Supports groups 1 through 4.

        List<long> droneEntities = new List<long>();
        IDictionary<long, BoundingBoxD> dronePositions = new Dictionary<long, BoundingBoxD>();

        string outText = ""; // Text buffer to avoid lag:tm:

        #region drone-specific

        long controlID = 0;
        long lastControllerPing = 0;

        static readonly double cos45 = Math.Sqrt(2) / 2;
        int ozoneRadius;

        int formation = 0;
        readonly Vector3D[][] formationPresets = new Vector3D[][] {
                            new Vector3D[] // X
                            {
                                // Max 32

                                // Ring 1
                                new Vector3D(formDistance, 0, 0),
                                new Vector3D(-formDistance, 0, 0),
                                new Vector3D(0, formDistance, 0),
                                new Vector3D(0, -formDistance, 0),
                                new Vector3D(cos45*formDistance, cos45*formDistance, 0),
                                new Vector3D(cos45*formDistance, -cos45*formDistance, 0),
                                new Vector3D(-cos45*formDistance, cos45*formDistance, 0),
                                new Vector3D(-cos45*formDistance, -cos45*formDistance, 0),

                                // Ring 2
                                new Vector3D(1.5*formDistance, 0, 0),
                                new Vector3D(-1.5*formDistance, 0, 0),
                                new Vector3D(0, 1.5*formDistance, 0),
                                new Vector3D(0, -1.5*formDistance, 0),
                                new Vector3D(1.5*cos45*formDistance, 1.5*cos45*formDistance, 0),
                                new Vector3D(1.5*cos45*formDistance, -1.5*cos45*formDistance, 0),
                                new Vector3D(-1.5*cos45*formDistance, 1.5*cos45*formDistance, 0),
                                new Vector3D(-1.5*cos45*formDistance, -1.5*cos45*formDistance, 0),

                                // Ring 3
                                new Vector3D(3.0*formDistance, 0, 0),
                                new Vector3D(-3.0*formDistance, 0, 0),
                                new Vector3D(0, 3.0*formDistance, 0),
                                new Vector3D(0, -3.0*formDistance, 0),
                                new Vector3D(3.0*cos45*formDistance, 3.0*cos45*formDistance, 0),
                                new Vector3D(3.0*cos45*formDistance, -3.0*cos45*formDistance, 0),
                                new Vector3D(-3.0*cos45*formDistance, 3.0*cos45*formDistance, 0),
                                new Vector3D(-3.0*cos45*formDistance, -3.0*cos45*formDistance, 0),

                                // Ring 4
                                new Vector3D(4.5*formDistance, 0, 0),
                                new Vector3D(-4.5*formDistance, 0, 0),
                                new Vector3D(0, 4.5*formDistance, 0),
                                new Vector3D(0, -4.5*formDistance, 0),
                                new Vector3D(4.5*cos45*formDistance, 4.5*cos45*formDistance, 0),
                                new Vector3D(4.5*cos45*formDistance, -4.5*cos45*formDistance, 0),
                                new Vector3D(-4.5*cos45*formDistance, 4.5*cos45*formDistance, 0),
                                new Vector3D(-4.5*cos45*formDistance, -4.5*cos45*formDistance, 0)
                            },
                            new Vector3D[] // Sphere
                            {
                                // Max 14

                                new Vector3D(formDistance, 0, 0),
                                new Vector3D(-formDistance, 0, 0),
                                new Vector3D(0, formDistance, 0),
                                new Vector3D(0, -formDistance, 0),
                                new Vector3D(0, 0, formDistance),
                                new Vector3D(0, 0, -formDistance),

                                new Vector3D(cos45*formDistance, cos45*formDistance, 0),
                                new Vector3D(cos45*formDistance, -cos45*formDistance, 0),
                                new Vector3D(-cos45*formDistance, cos45*formDistance, 0),
                                new Vector3D(-cos45*formDistance, -cos45*formDistance, 0),
                                new Vector3D(0, cos45*formDistance, cos45*formDistance),
                                new Vector3D(0, cos45*formDistance, -cos45*formDistance),
                                new Vector3D(0, -cos45*formDistance, cos45*formDistance),
                                new Vector3D(0, -cos45*formDistance, -cos45*formDistance)
                            },
                            new Vector3D[] // V
                            {
                                new Vector3D(formDistance/2, 0, -formDistance/2),
                                new Vector3D(-formDistance/2, 0, -formDistance/2),
                                new Vector3D(0, formDistance/2, -formDistance/2),
                                new Vector3D(0, -formDistance/2, -formDistance/2),
                                new Vector3D(formDistance, 0, 0),
                                new Vector3D(-formDistance, 0, 0),
                                new Vector3D(0, formDistance, 0),
                                new Vector3D(0, -formDistance, 0),
                            },
                            new Vector3D[] // duckling formation
                            {
                                new Vector3D(0, 0, formDistance),
                                new Vector3D(0, 0, 2*formDistance),
                                new Vector3D(0, 0, 3*formDistance),
                                new Vector3D(0, 0, 4*formDistance),
                                new Vector3D(0, 0, 5*formDistance),
                                new Vector3D(0, 0, 6*formDistance),
                                new Vector3D(0, 0, 7*formDistance),
                                new Vector3D(0, 0, 2*formDistance),
                            },
        };

        string damageAmmo = "";
        string healAmmo = "";
        bool healController = false;

        MatrixD ctrlMatrix = new MatrixD();
        bool isFortified = false;
        bool isIntegrity = false;
        ITerminalAction toggleFort = null;
        ITerminalAction toggleIntegrity = null;
        Vector3D predictedTargetPos = new Vector3D(); // target position + lead
        Vector3D controllerPos = new Vector3D(); // center of controller ship
        Vector3D anchorVelocity = new Vector3D(); // velocity of "anchored" target; controller if in WINGMAN mode, target if in MAIN mode.
        Vector3D closestCollision = new Vector3D();
        Vector3D ctrlTargetPos = new Vector3D();
        double totalRuntime = 0;
        Vector3D resultPos = new Vector3D();
        double distanceTarget = 0;
        bool hasABs = false;

        double speed = 0;
        int id = -1; // Per-drone ID. Used for formation flight. Controller is always -1

        #endregion

        #region Blocks

        List<IMyTerminalBlock> allABs = new List<IMyTerminalBlock>();
        List<IMyTerminalBlock> forwardAB = new List<IMyTerminalBlock>();
        List<IMyTerminalBlock> backAB = new List<IMyTerminalBlock>();
        List<IMyTerminalBlock> upAB = new List<IMyTerminalBlock>();
        List<IMyTerminalBlock> downAB = new List<IMyTerminalBlock>();
        List<IMyTerminalBlock> leftAB = new List<IMyTerminalBlock>();
        List<IMyTerminalBlock> rightAB = new List<IMyTerminalBlock>();
        List<IMyTerminalBlock> fixedGuns = new List<IMyTerminalBlock>();
        List<IMyThrust> allThrust = new List<IMyThrust>();
        IMyRadioAntenna antenna;

        IMyTerminalBlock shieldController = null;
        IMyTerminalBlock shieldModulator = null;

        /* 
         * 0 forward
         * 1 back
         * 2 up
         * 3 down
         * 4 right
         * 5 left
         */
        double[] thrustAmt = new double[6];
        List<IMyThrust> forwardThrust = new List<IMyThrust>();
        List<IMyThrust> backThrust = new List<IMyThrust>();
        List<IMyThrust> upThrust = new List<IMyThrust>();
        List<IMyThrust> downThrust = new List<IMyThrust>();
        List<IMyThrust> leftThrust = new List<IMyThrust>();
        List<IMyThrust> rightThrust = new List<IMyThrust>();

        List<IMyTextPanel> outLcds = new List<IMyTextPanel>();

        List<IMyTerminalBlock> madars = new List<IMyTerminalBlock>();

        #endregion


        public Program()
        {
            try
            {
                Init();
            }
            catch (Exception e)
            {
                Echo("[color=#FFFF0000]!!! If you can see this, you're probably missing something important (i.e. antenna) !!![/color]");
                Echo(e.Message);
            }

            if (isController)
                SendGroupMsg<String>("stop", true);
        }

        public void Init()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update100;

            // Init Weaponcore API
            wAPI = new WcPbApi();
            canRun = wAPI.Activate(Me);
            if (!canRun) return;
            Echo("Initialized wAPI");

            // Init Defense Shields API
            dAPI = new PbApiWrapper(Me);
            Echo("Initialized dsAPI");

            // Init Debug Draw API
            d = new DebugAPI(this);
            Echo("Initialized debugAPI");

            // Squares zoneRadius to avoid Vector3D.Distance() calls. Roots are quite unperformant.
            zoneRadius *= zoneRadius;
            ozoneRadius = (int)Math.Sqrt(zoneRadius * 0.95);

            if (!isController)
            {
                // Check for existing group config
                if (Me.CustomData == "" || Me.CustomData.Length > 2) Me.CustomData = "1";
                int.TryParse(Me.CustomData, out group);
                if (group == -1)
                {
                    isController = true; // IK this looks dumb. Goal is to make sure isController doesn't get accidentally set to false.
                    group = 0;
                }
            }
            else Me.CustomData = "-1";
            Echo($"Checked customdata for group ({group})");

            // Init Whip's GPS Gyro Control
            gridSystem = GridTerminalSystem;
            gridId = Me.CubeGrid.EntityId;
            gyros = new GyroControl(Me, kP, kI, kD, lowerBound, upperBound, timeStep);
            Echo("Initialized Whip's Gyro Control");

            // Get cockpit for controller
            GridTerminalSystem.GetBlocksOfType<IMyCockpit>(null, b => { cockpit = b; return true; });
            Echo("Searched for cockpit " + (cockpit == null ? "null" : cockpit.CustomName));

            // Gets shield controllers. Also disables autofortify if no shield controllers are found.
            List<IMyTerminalBlock> shieldControllers = new List<IMyTerminalBlock>();
            List<IMyTerminalBlock> shieldModulators = new List<IMyTerminalBlock>();
            bool hasEnhancer = false;
            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(shieldControllers, b => { if (!hasEnhancer) hasEnhancer = b.DefinitionDisplayNameText.Contains("Enhancer"); return b.CustomName.Contains("Shield Controller"); });
            Echo($"Located {shieldControllers.Count} shield controllers");
            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(shieldModulators, b => { return b.CustomName.Contains("Shield Modulator"); });
            Echo($"Located {shieldModulators.Count} shield modulators");
            if (autoFortify) autoFortify = hasEnhancer;

            // Check if shield controller exists, set up shortcut actions for Fortify and Integrity.
            try
            {
                shieldController = shieldControllers[0];
                shieldController.GetActions(new List<ITerminalAction>(), b => { if (b.Id == "DS-C_ShieldFortify_Toggle") toggleFort = b; return true; });
                Echo("Found ShieldFortify action");
            }
            catch
            {
                Echo("[color=#FFFF0000]No shield controller![/color]");
            }

            // Check if shield controller exists, set up shortcut actions for Fortify and Integrity.
            try
            {
                shieldModulator = shieldModulators[0];
                if (autoIntegrity) autoIntegrity = shieldModulator != null;
                shieldModulator.GetActions(new List<ITerminalAction>(), b => { if (b.Id == "DS-M_ModulateReInforceProt_Toggle") toggleIntegrity = b; return true; });
                Echo("Found ModulateReInforceProt action");
            }
            catch
            {
                Echo("[color=#FFFF0000]No shield modulator![/color]");
            }

            // Autosets mass if ship controller detected
            GridTerminalSystem.GetBlocksOfType<IMyShipController>(null, b => { mass = b.CalculateShipMass().TotalMass; return true; });
            Echo("Set grid mass to " + mass);

            // Set antenna ranges to 25k (save a tiny bit of power)
            GridTerminalSystem.GetBlocksOfType<IMyRadioAntenna>(null, b => { b.Radius = 25000; antenna = b; return true; });
            Echo("Set antenna radii to 25km");

            // Get LCDs with name containing 'Rocketman' and sets them up
            if (isController || debug) GridTerminalSystem.GetBlocksOfType(outLcds, b => b.CustomName.ToLower().Contains("rocketman"));
            foreach (var l in outLcds)
            {
                l.ContentType = ContentType.TEXT_AND_IMAGE;
                if (!l.CustomData.Contains("hudlcd")) l.CustomData = "hudlcd";
            }
            Echo($"Found {outLcds.Count} LCDs");

            // Get fixed guns
            try
            {
                GridTerminalSystem.GetBlockGroupWithName(gunGroup).GetBlocksOfType<IMyTerminalBlock>(fixedGuns, b => wAPI.HasCoreWeapon(b));
            }
            catch
            {
                doCcrp = false;
            }
            Echo($"Found {fixedGuns.Count} fixed weapons");

            try
            {
                if (fixedGuns.Count > 0)
                {
                    string[] splitCustomData = new string[2];
                    IMyTerminalBlock b = fixedGuns[0];
                    if (b.CustomData != "")
                    {
                        splitCustomData = b.CustomData.Split('\n');
                        healAmmo = splitCustomData[0];
                        damageAmmo = splitCustomData[1];

                        Echo($"[color=#FFFFFF00]Set ammo types to:\n    HEAL - {healAmmo}\n    DAMAGE - {damageAmmo}[/color]");
                    }
                }
            }
            catch { }

            // Get afterburners
            try
            {
                GridTerminalSystem.GetBlockGroupWithName(abGroup).GetBlocksOfType<IMyTerminalBlock>(allABs, b => wAPI.HasCoreWeapon(b));
            }
            catch
            {
                Echo("[color=#FFFFAA00]No afterburners detected![/color]");
            }
            Echo($"Found {allABs.Count} afterburners");

            // Sort afterburners
            RecalcABs();
            Echo($"Sorted {allABs.Count} afterburners");
            
            // Get all thrust
            GridTerminalSystem.GetBlocksOfType(allThrust);
            Echo($"Found {allThrust.Count} thrusters");

            // Sort thrust and calculate total thrust per direction
            RecalcThrust();
            Echo($"Sorted {allThrust.Count} thrusters");

            // Set gyro and thrust override to 0
            gyros.Reset();
            SetThrust(-1f, allThrust, false);
            Echo("Reset thrust and gyro control");

            // Init IGC (Inter-Grid Communications) listener
            myBroadcastListener = IGC.RegisterBroadcastListener(isController ? "-1" : group.ToString());
            Echo("Inited myBroadcastListener");

            positionListener = IGC.RegisterBroadcastListener("pos" + (multipleControllers ? group.ToString() : ""));
            Echo("Inited positionListener");

            velocityListener = IGC.RegisterBroadcastListener("vel" + (multipleControllers ? group.ToString() : ""));
            Echo("Inited velocityListener");

            orientListener = IGC.RegisterBroadcastListener("ori" + (multipleControllers ? group.ToString() : ""));
            Echo("Inited orientListener");

            performanceListener = IGC.RegisterBroadcastListener("per");
            Echo("Inited performanceListener");

            dronePosListener = IGC.RegisterBroadcastListener("dpos");
            Echo("Inited dronePosListener");

            antenna.HudText = "Awaiting " + (isController ? "Drones!" : "Controller!");

            if (isController)
            {
                SendGroupMsg<String>("r", true); // Reset drone IDs
                SendGroupMsg<String>("m" + mainDistance, true);
                SendGroupMsg<String>("o" + formDistance, true);
                Echo("Reset drone IDs and shared formation distances");
            }

            try
            {
                if (autoFortify && shieldController.CustomData == "1") toggleFort.Apply(shieldController);
            }
            catch { }

            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(madars, b => b.DefinitionDisplayNameText == "MADAR");
            Echo($"Found {madars.Count()} MADARs.");

            // Convert maxOffset from human-readable format to dot product
            maxOffset = Math.Cos(maxOffset / 57.2957795);
            Echo("Set maxOffset to " + maxOffset);

            gridId = Me.CubeGrid.EntityId;
            frame = 0;

            // Sets PB surface to text for error reporting.
            Me.GetSurface(0).ContentType = ContentType.TEXT_AND_IMAGE;

            Echo("[color=#FF00FF00]\nSuccessfully initialized as a [/color]" + (isController ? "controller." : "drone."));
        }

        public bool IGCHandler(UpdateType updateSource)
        {
            bool wasMessageRecieved = false;
            // If IGC message recieved
            try
            {
                while (myBroadcastListener.HasPendingMessage)
                {
                    MyIGCMessage message = myBroadcastListener.AcceptMessage();

                    if (message.Data.GetType() == typeof(long) && !isController)
                    {
                        wAPI.SetAiFocus(Me, message.As<long>());
                    }
                    else if (message.Data.GetType() == typeof(Vector3D))
                        ctrlTargetPos = message.As<Vector3D>();
                    else
                        ParseCommands(message.Data.ToString(), updateSource);

                    wasMessageRecieved = true;
                }

                while (positionListener.HasPendingMessage)
                {
                    controllerPos = positionListener.AcceptMessage().As<Vector3D>();
                    if (!activated && id != -1)
                    {
                        activated = true;
                        centerOfGrid = Me.CubeGrid.GetPosition();
                        Runtime.UpdateFrequency = UpdateFrequency.Update1;
                    }
                    wasMessageRecieved = true;
                }

                if (mode == 1)
                {
                    while (velocityListener.HasPendingMessage)
                    {
                        anchorVelocity = velocityListener.AcceptMessage().As<Vector3D>();
                        wasMessageRecieved = true;
                    }
                }

                while (orientListener.HasPendingMessage)
                {
                    ctrlMatrix = orientListener.AcceptMessage().As<MatrixD>();
                    wasMessageRecieved = true;
                }

                while (performanceListener.HasPendingMessage)
                {
                    totalRuntime += performanceListener.AcceptMessage().As<double>();
                    wasMessageRecieved = true;
                }

                while (dronePosListener.HasPendingMessage)
                {
                    MyIGCMessage m = dronePosListener.AcceptMessage();
                    dronePositions[m.Source] = m.As<BoundingBoxD>();
                    wasMessageRecieved = true;
                }
            }
            catch
            {

            }
            return wasMessageRecieved;
        }

        public void AutoIntegrity()
        {
            // AutoIntegrity System

            if (autoIntegrity && dAPI.GridHasShield(Me.CubeGrid) && !isController)
            {
                try
                {
                    if (distanceTarget < 40000 && !isIntegrity) // fuck you darkstar (x2). distanceTarget = distance squared.
                    {
                        toggleIntegrity.Apply(shieldModulator);
                        isIntegrity = true; // "dead reckoning" system for autointegrity. Breaks if messed with. )))
                    }
                    else if (isIntegrity && distanceTarget > 40000)
                    {
                        toggleIntegrity.Apply(shieldModulator);
                        isIntegrity = false;
                    }
                }
                catch { }
            }
        }

        public void AutoFortify()
        {
            // Autofortify system

            if (autoFortify && dAPI.GridHasShield(Me.CubeGrid))
            {
                try
                {
                    if (speed < 12 && !isFortified) // fuck you darkstar
                    {
                        shieldController.CustomData = "1";
                        toggleFort.Apply(shieldController);
                        isFortified = true; // "dead reckoning" system for autofortify. Breaks if messed with. )))
                    }
                    else if (isFortified && speed > 12)
                    {
                        shieldController.CustomData = "0";
                        toggleFort.Apply(shieldController);
                        isFortified = false;
                    }
                }
                catch { }
            }
        }

        public void IGCSendHandler()
        {
            if (wAPI.GetAiFocus(gridId).Value.EntityId != 0)
                SendGroupMsg<Vector3D>(wAPI.GetAiFocus(gridId).Value.Position, false);

            if (frame % 20 == 0)
            {
                if (wAPI.GetAiFocus(gridId).Value.EntityId != 0)
                    SendGroupMsg<long>(wAPI.GetAiFocus(gridId).Value.EntityId, !multipleControllers || group == 0);

                if (frame % 240 == 0)
                {
                    SendGroupMsg<String>("c" + Me.CubeGrid.EntityId, true);
                }
            }

            if ((mode == 1) && frame % 2 == 0)
            {
                MatrixD m = cockpit.IsFunctional ? cockpit.WorldMatrix : Me.WorldMatrix;
                //d.DrawLine(centerOfGrid, centerOfGrid + m.Up * 100, Color.Blue, 0.1f);

                if (rotate)
                    m *= Matrix.CreateFromAxisAngle(m.Forward, (frame / rotateDividend % 360) / 57.2957795f);

                if (multipleControllers)
                {
                    if (group != 0)
                    {
                        IGC.SendBroadcastMessage("pos" + group, centerOfGrid);
                        IGC.SendBroadcastMessage("vel" + group, -Me.CubeGrid.LinearVelocity);

                        IGC.SendBroadcastMessage("ori" + group, m);
                    }
                    else
                    {
                        for (int i = 1; i < 5; i++)
                        {
                            IGC.SendBroadcastMessage("pos" + i, centerOfGrid);
                            IGC.SendBroadcastMessage("vel" + i, -Me.CubeGrid.LinearVelocity);

                            IGC.SendBroadcastMessage("ori" + i, m);
                        }
                    }
                }
                else
                {
                    // Transmit position and orientation every other tick
                    IGC.SendBroadcastMessage("pos", centerOfGrid);
                    IGC.SendBroadcastMessage("vel", -Me.CubeGrid.LinearVelocity);
                    IGC.SendBroadcastMessage("ori", m);
                }
            }
        }

        public long GetClosestTarget()
        {
            long target = 0;
            Dictionary<MyDetectedEntityInfo, float> targets = new Dictionary<MyDetectedEntityInfo, float>();
            wAPI.GetSortedThreats(Me, targets);

            foreach (var targ in targets.Keys) // goofy ahh distance sorter
            {
                double dist2 = Vector3D.DistanceSquared(targ.Position, centerOfGrid);
                if (dist2 < distanceTarget || distanceTarget == 0)
                {
                    distanceTarget = dist2;
                    target = targ.EntityId;
                }
            }

            return target;
        }

        public void CCRPHandler()
        {
            if (healController)
            {
                outText += "Locked on controller\n";

                bool isLinedUp = Vector3D.Normalize(controllerPos - centerOfGrid).Dot(Me.WorldMatrix.Forward) > maxOffset;

                foreach (var weapon in fixedGuns)
                {
                    //d.DrawLine(centerOfGrid, Me.WorldMatrix.Forward * wAPI.GetMaxWeaponRange(weapon, 0) + centerOfGrid, Color.White, 0.5f);
                    if (isLinedUp && wAPI.IsWeaponReadyToFire(weapon))
                    {
                        if (!weapon.GetValueBool("WC_Shoot"))
                        {
                            weapon.SetValueBool("WC_Shoot", true);
                        }
                    }
                    else if (weapon.GetValueBool("WC_Shoot"))
                    {
                        //wAPI.ToggleWeaponFire(weapon, false, true);
                        weapon.SetValueBool("WC_Shoot", false);
                    }
                }
            }
            else if (!resultPos.IsZero())
            {
                foreach (var weapon in fixedGuns)
                {
                    // See variable name
                    try
                    {
                        predictedTargetPos = wAPI.GetPredictedTargetPosition(weapon, aiTarget.EntityId, 0).Value;
                    }
                    catch
                    {
                        continue;
                    }

                    LineD weaponRay = new LineD(Me.CubeGrid.GetPosition(), Me.WorldMatrix.Forward * wAPI.GetMaxWeaponRange(weapon, 0) + centerOfGrid);

                    //bool isLinedUp = Vector3D.Normalize(predictedTargetPos - centerOfGrid).Dot(Me.WorldMatrix.Forward) > maxOffset;
                    BoundingBoxD box = aiTarget.BoundingBox.Translate(predictedTargetPos - aiTarget.Position);
                    bool isLinedUp = box.Intersects(ref weaponRay);

                    //d.DrawGPS("Lead Position", predictedTargetPos, Color.Red);
                    //d.DrawLine(centerOfGrid, Me.WorldMatrix.Forward * wAPI.GetMaxWeaponRange(weapon, 0) + centerOfGrid, isLinedUp ? Color.Red : Color.White, 0.5f);
                    //d.DrawAABB(box, isLinedUp ? Color.Red : Color.White);

                    // Checks if weapon is aligned, and within range. (Uses DistanceSquared for performance reasons [don't do sqrt, kids])
                    float r = wAPI.GetMaxWeaponRange(weapon, 0);

                    Fire(weapon, isLinedUp && r * r > distanceTarget && wAPI.IsWeaponReadyToFire(weapon));
                }
            }
        }

        void Fire(IMyTerminalBlock weapon, bool enabled)
        {
            if (enabled)
            {
                if (!weapon.GetValueBool("WC_Shoot"))
                {
                    weapon.SetValueBool("WC_Shoot", true);
                }
            }
            else if (weapon.GetValueBool("WC_Shoot"))
            {
                weapon.SetValueBool("WC_Shoot", false);
            }
        }

        int errorCounter = 0;
        public void Main(string argument, UpdateType updateSource)
        {
            // Prevents running multiple times per tick
            if (updateSource == UpdateType.IGC)
                return;

            //d.RemoveAll();

            if (!canRun) // If unable to init WC api, do not run.
            {
                Echo("[color=#FFFF0000]UNABLE TO RUN! Make sure Weaponcore is enabled.[/color]");

                frame++;
                if (frame >= 10)
                    Init();
                return;
            }
            else if ((!isController || mode == 1) && !activated) // Controller doesn't need to be running constantly, unless in wingman mode. That was a lie to children. Actually it wasn't. AAAAHHHHHHH.
                Runtime.UpdateFrequency = UpdateFrequency.Update100;

            if (IGCHandler(updateSource))
                lastControllerPing = DateTime.Now.Ticks;

            try
            {
                if (argument.Length > 0) ParseCommands(argument.ToLower(), updateSource);
            }
            catch { }

            // Status info
            outText += $"[M{mode} : G{group} : ID{id}] {(activated ? "[color=#FF00FF00]ACTIVE[/color]" : "[color=#FFFF0000]INACTIVE[/color]")} {IndicateRun()}\n\nRocketman Drone Manager\n-------------------------\n{(isController ? $"Controlling {droneEntities.Count} drone(s)" : "Drone Mode")}\n";
            
            //if (!isController)
                //d.PrintHUD(RoundPlaces((DateTime.Now.Ticks - lastControllerPing) / 10000000, 2) + "s");

            // If ID unset and is not controller, ping controller for ID.
            if (id == -1 && !isController)
            {
                activated = false;
                IGC.SendBroadcastMessage("-1", "e" + Me.CubeGrid.EntityId);
                return;
            }

            if (activated) // Generic "I'm on!" stuff
            {
                try
                {
                    // Share World Axis-Aligned Bounding Box for collision avoidance
                    IGC.SendBroadcastMessage("dpos", Me.CubeGrid.WorldAABB);

                    centerOfGrid = Me.CubeGrid.GetPosition();

                    aiTarget = wAPI.GetAiFocus(gridId).Value;


                    outText += "Velocity " + speed + "\n";

                    if (frame % 60 == 0)
                    {
                        speed = Me.CubeGrid.LinearVelocity.Length();

                        AutoFortify();

                        AutoIntegrity();

                        // Revengance status
                        if (!isController && !(mode == 0 && autoTarget) && !healController) // If drone and hasn't already gone ballistic
                        {
                            if (DateTime.Now.Ticks - lastControllerPing > 100000000) // If hasn't recieved message within 10 seconds
                            {
                                mode = 0;          // Go into freeflight mode
                                autoTarget = true; // Automatically attack the nearest enemy
                                antenna.HudText = new Random().NextDouble() > 0.5 ? "[ANGERY]" : "IT'S JOEVER"; // Ree at the enemy
                            }
                        }
                    }

                    if (isController) // If on AND is controller, Update drones with new instructions/positions
                    {
                        IGCSendHandler();
                    }

                    else // If on AND is drone
                    {
                        RunActiveDrone();
                    }

                    errorCounter = 0;
                }

                catch (Exception e)
                {
                    if (errorCounter > 10)
                    {
                        if (antenna != null)
                            antenna.HudText += " [CRASHED]";
                        throw e;
                    }

                    Me.GetSurface(0).WriteText(e.ToString());

                    errorCounter++;
                }
            }

            if (debug) // Print runtime info
                PrintDebugText();

            foreach (var l in outLcds)
                l.WriteText(outText);

            outText = "";

            frame++;
        }

        private void ActiveDroneFrame2()
        {
            // Gets closest target. AHHHHHHHHHHHHHHHHHHHHHHHHHH.
            if (!healController && (mode == 0 || autoTarget) && frame % 60 == 0)
                wAPI.SetAiFocus(Me, GetClosestTarget());

            resultPos = aiTarget.IsEmpty() ? ctrlTargetPos : aiTarget.Position;

            if (doCcrp)
                CCRPHandler();
            else
                outText += "CCRP not running! " + (doCcrp ? "NO TARGET" : "DISABLED") + "\n";

            if (frame % 60 == 0)
            {
                // turn off guns if target is missing
                if (aiTarget.IsEmpty())
                    foreach (var weapon in fixedGuns)
                        Fire(weapon, false);

                bool needsRecalc = false;
                // check if any thrusters are dead
                foreach (var t in allThrust)
                {
                    if (t.WorldAABB == new BoundingBoxD())
                    {
                        needsRecalc = true;
                        break;
                    }
                }
                if (needsRecalc)
                {
                    GridTerminalSystem.GetBlocksOfType(allThrust);
                    RecalcThrust();
                    outText += $"Recalculated thrust with {allThrust.Count} thrusters";
                }
            }
        }

        private bool nearZone = false;

        private void RunActiveDrone()
        {
            outText += "Locked on target " + aiTarget.Name + "\n";

            if (frame % 2 == 0)
                ActiveDroneFrame2();

            Vector3D vecToEnemy = doCcrp && !resultPos.IsZero() ? Vector3D.Normalize(predictedTargetPos - centerOfGrid) : ctrlMatrix.Forward;
            Vector3D moveTo = new Vector3D();

            Vector3D stopPosition = CalcStopPosition(-Me.CubeGrid.LinearVelocity, centerOfGrid);
            //d.DrawLine(centerOfGrid, resultPos, Color.Red, 0.1f);
            //d.DrawGPS("Stop Position", stopPosition);

            nearZone = stopPosition.LengthSquared() > zoneRadius * (nearZone ? 0.95 : 1);

            // Autostop when near zone
            if (nearZone)
            {
                //d.PrintHUD("YOU BLOODY IDIOT, YOU MADE ME GO OUT OF THE ZONE");
                antenna.HudText += " [ZONE]";
                ThrustControl(centerOfGrid, upThrust, downThrust, leftThrust, rightThrust, forwardThrust, backThrust);
            }

            else
            {
                switch (mode)
                {
                    case 0: // Orbit Enemy

                        gyros.FaceVectors(vecToEnemy, Me.WorldMatrix.Up);
                        moveTo = aiTarget.Position + Vector3D.Rotate(formationPresets[1][id] / formDistance * mainDistance, ctrlMatrix);

                        // fucking ram the enemy, idc. they probably deserve it. murdered some cute baby kittens or whatever.
                        //d.DrawLine(centerOfGrid, moveTo, Color.Blue, 0.1f);
                        closestCollision = CheckCollision(moveTo);

                        if (closestCollision != new Vector3D())
                            moveTo += moveTo.Cross(closestCollision);
                        break;

                    case 1: // Orbit Controller

                        double dSq = Vector3D.DistanceSquared(controllerPos, Me.CubeGrid.GetPosition());

                        if (!healController && damageAmmo != "")
                        {
                            //d.PrintHUD($"Damage ammo {damageAmmo}");
                            foreach (var wep in fixedGuns)
                            {
                                if (wAPI.GetActiveAmmo(wep, 0) == damageAmmo)
                                    break;
                                wep.SetValue<Int64>("WC_PickAmmo", 0);
                            }
                        }

                        if (dSq > formDistance * formDistance * 4 || healController)
                            vecToEnemy = Vector3D.Normalize(controllerPos - centerOfGrid);


                        gyros.FaceVectors(vecToEnemy, Me.WorldMatrix.Up);
                        // Prevents flipping when controllerFwd is negative
                        //moveTo = controllerPos + Vector3D.Rotate(controllerFwd.Sum > 0 ? formationPresets[formation][id] : -formationPresets[formation][id], MatrixD.CreateFromDir(controllerFwd));
                        moveTo = controllerPos + Vector3D.Rotate(formationPresets[formation][id], ctrlMatrix);

                        // Check if controller is in the way. If so, avoid
                        //if (Vector3D.DistanceSquared(centerOfGrid, controllerPos) < Vector3D.DistanceSquared(centerOfGrid, moveTo)) moveTo += controllerFwd * formDistance;

                        //d.DrawLine(centerOfGrid, controllerPos, Color.Green, 0.1f);
                        //d.DrawLine(centerOfGrid, moveTo, Color.Blue, 0.1f);
                        //d.DrawGPS("Drone Position", moveTo);

                        closestCollision = CheckCollision(moveTo);

                        if (closestCollision != new Vector3D())
                            moveTo += moveTo.Cross(closestCollision);
                        break;

                    case 2: // Sit and Fortify

                        gyros.FaceVectors(vecToEnemy, Me.WorldMatrix.Up);

                        moveTo = controllerPos + formationPresets[formation][id];

                        closestCollision = CheckCollision(moveTo);

                        if (closestCollision != new Vector3D())
                            moveTo += moveTo.Cross(closestCollision);
                        break;
                }

                moveTo = Vector3D.ClampToSphere(moveTo, ozoneRadius);

                ThrustControl(stopPosition - moveTo, upThrust, downThrust, leftThrust, rightThrust, forwardThrust, backThrust);
            }
        }

        private void PrintDebugText()
        {
            outText += $"{Runtime.CurrentInstructionCount} instructions @ {Runtime.LastRunTimeMs}ms\n";
            if (isController) outText += $"Total: {(int)(totalRuntime / 16 * 100000) / 100000d}% ({totalRuntime}ms)";
            if (frame % 4 == 0)
            {

                if (!isController)
                    IGC.SendBroadcastMessage("per", Runtime.LastRunTimeMs);
                else
                {
                    if (totalRuntime != Runtime.LastRunTimeMs)
                    {
                        totalRuntime = 0;
                        totalRuntime += Runtime.LastRunTimeMs;
                    }
                }
            }

            Echo(outText);
        }

        MyDetectedEntityInfo? bufferMadarInfo = new MyDetectedEntityInfo();

        public Vector3D CheckCollision(Vector3D stopPosition)
        {
            // holy mother of jank
            foreach (var m in madars)
            {
                bufferMadarInfo = wAPI.GetWeaponTarget(m, 22);
                if (bufferMadarInfo.HasValue && !dronePositions.ContainsKey(bufferMadarInfo.Value.EntityId))
                    dronePositions.Add(bufferMadarInfo.Value.EntityId, bufferMadarInfo.Value.BoundingBox);

                bufferMadarInfo = wAPI.GetWeaponTarget(m, 23);
                if (bufferMadarInfo.HasValue && !dronePositions.ContainsKey(bufferMadarInfo.Value.EntityId))
                    dronePositions.Add(bufferMadarInfo.Value.EntityId, bufferMadarInfo.Value.BoundingBox);

                bufferMadarInfo = wAPI.GetWeaponTarget(m, 24);
                if (bufferMadarInfo.HasValue && !dronePositions.ContainsKey(bufferMadarInfo.Value.EntityId))
                    dronePositions.Add(bufferMadarInfo.Value.EntityId, bufferMadarInfo.Value.BoundingBox);
            }

            LineD r = new LineD(Me.CubeGrid.GetPosition(), stopPosition);

            foreach (var p in dronePositions.Values)
            {
                if (p.Intersects(ref r))
                {
                    return p.Center;
                }
            }
            return new Vector3D();
        }

        public void ParseCommands(string argument, UpdateType updateSource)
        {
            if (argument == "") return;


            switch (argument)
            {
                case "main":
                    mode = 0;
                    healController = false;
                    if (isController)
                        SendGroupMsg<String>("main", false);
                    antenna.HudText = id.ToString() + " | " + mode.ToString() + group.ToString();
                    return;
                case "wing":
                    mode = 1;
                    healController = false;
                    if (isController)
                        SendGroupMsg<String>("wing", false);
                    antenna.HudText = id.ToString() + " | " + mode.ToString() + group.ToString();
                    return;
                case "fort":
                    mode = 2;
                    healController = false;
                    if (isController)
                    {
                        SendGroupMsg<String>("fort", false);
                        IGC.SendBroadcastMessage("pos", centerOfGrid);
                        MatrixD m = cockpit.IsFunctional ? cockpit.WorldMatrix : Me.WorldMatrix;
                        
                        IGC.SendBroadcastMessage("ori", m);
                    }
                    antenna.HudText = id.ToString() + " | " + mode.ToString() + group.ToString();
                    return;
                case "start":
                    activated = true;
                    centerOfGrid = Me.CubeGrid.GetPosition();
                    lastControllerPing = DateTime.Now.Ticks;
                    Runtime.UpdateFrequency = UpdateFrequency.Update1;
                    if (isController)
                    {
                        if (updateSource != UpdateType.IGC)
                        {
                            SendGroupMsg<String>("start", true);
                            IGC.SendBroadcastMessage("-1", "start");
                        }
                        IGC.SendBroadcastMessage("pos", centerOfGrid);

                        IGC.SendBroadcastMessage("ori", cockpit.IsFunctional ? cockpit.WorldMatrix : Me.WorldMatrix);
                    }
                    return;
                case "group":
                    if (group < 4) group++; else group = 0;
                    antenna.HudText = id.ToString() + " | " + mode.ToString() + group.ToString();
                    return;
                case "stop":
                    activated = false;
                    SetThrust(-1f, allThrust, false);
                    gyros.Reset();
                    if (doCcrp)
                    {
                        foreach (var weapon in fixedGuns)
                            Fire(weapon, false);
                    }
                    if (isController && updateSource != UpdateType.IGC)
                    {
                        SendGroupMsg<String>("stop", true);
                        IGC.SendBroadcastMessage("-1", "stop");
                    }
                    return;
                case "heal":
                    mode = 1;

                    if (isController)
                    {
                        SendGroupMsg<String>("heal", false);
                    }
                    else
                    {
                        if (healAmmo != "") healController = true;
                        else return;
                        wAPI.SetAiFocus(Me, controlID);
                        //d.PrintHUD($"Healing ammo {healAmmo}");
                        foreach (var wep in fixedGuns)
                        {
                            wep.SetValue<Int64>("WC_PickAmmo", 1);
                        }
                    }
                    return;
                case "learnheal":
                    foreach (var w in fixedGuns)
                    {
                        healAmmo = wAPI.GetActiveAmmo(w, 0);
                        if (damageAmmo != "") w.CustomData = healAmmo + "\n" + damageAmmo;
                    }
                    Echo("[color=#FF00FF00]Learned damage ammo [/color]" + healAmmo);
                    break;
                case "learndamage":
                    foreach (var w in fixedGuns)
                    {
                        damageAmmo = wAPI.GetActiveAmmo(w, 0);
                        if (healAmmo != "") w.CustomData = healAmmo + "\n" + damageAmmo;
                    }
                    Echo("[color=#FF00FF00]Learned damage ammo [/color]" + damageAmmo);
                    break;
                case "ctrlgroup":
                    if (isController)
                    {
                        SendGroupMsg<string>("c" + Me.CubeGrid.EntityId, false);
                    }
                    break;
            }

            if (isController)
            {
                if (argument.Substring(0, 4) == "form")
                {
                    int.TryParse(argument.Substring(4), out formation);
                    SendGroupMsg<String>("f" + formation.ToString(), false);
                }
            }
            

            /* Internal comms:
             *  I - Used to set drone ID.        | I(int ID)(long EntityID)
             *  E - Send EntityID to controller. | E(long EntityID)
             *  R - Reset drone IDs.             | R
             *  F - Formation preset.            | F(Int preset)
             *  O - Set distance from controller.| O(int formDistance)
             *  M - Set distance from target.    | M(int mainDistance)
             *  
             */
            switch (argument[0])
            {
                case 'i':
                    if (!isController && long.Parse(argument.Substring(3)) == Me.CubeGrid.EntityId)
                        id = int.Parse(argument.Substring(1, 2));
                    antenna.HudText = id.ToString() + " | " + mode.ToString() + group.ToString();
                    break;
                case 'e':
                    if (isController)
                    {
                        long tId = long.Parse(argument.Substring(1));
                        if (!droneEntities.Contains(tId)) droneEntities.Add(tId);
                        for (int i = 0; i < droneEntities.Count; i++)
                        {
                            SendGroupMsg<String>("i" + (i < 10 ? "0" + i.ToString() : i.ToString()) + droneEntities[i], true); // Send message in format: i04EntityId
                        }
                        antenna.HudText = id.ToString() + " | " + mode.ToString() + group.ToString();
                    }
                    break;
                case 'r':
                    if (!isController)
                    {
                        id = -1;
                    }
                    break;
                case 'm':
                    if (!isController)
                    {
                        int.TryParse(argument.Substring(1), out mainDistance);
                    }
                    break;
                case 'o':
                    if (!isController)
                    {
                        int.TryParse(argument.Substring(1), out formDistance);
                    }
                    break;
                case 'f':
                    int.TryParse(argument.Substring(1), out formation);
                    if (formation + 1 > formationPresets.Length) formation = formationPresets.Length - 1;
                    break;
                //default:
                //    long targetID;
                //    if (mode != 0 && !autoTarget && long.TryParse(argument, out targetID))
                //    {
                //        //d.PrintChat("Set target to " + targetID);
                //        wAPI.SetAiFocus(Me, targetID);
                //    }
                //    break;
            }
        }

        Vector3D accel = new Vector3D();
        Vector3D accelR = new Vector3D();
        Vector3D timeToStop = new Vector3D();

        public Vector3D CalcStopPosition(Vector3D velocity, Vector3D gridCenter)
        {
            // Calculate acceleration for each (local) axis; 6 possible sides with thrust
            accel = new Vector3D(thrustAmt[4], thrustAmt[2], -thrustAmt[0]) / mass;
            accelR = new Vector3D(-thrustAmt[5], -thrustAmt[3], thrustAmt[1]) / mass;

            // Rotate (global -> local) velocity because Vector Math:tm:
            Vector3D rVelocity = Vector3D.Rotate(velocity, Me.WorldMatrix);

            // Calculate time to stop for each (local) axis
            timeToStop = new Vector3D(
                accel.X + rVelocity.X < rVelocity.X - accelR.X ? rVelocity.X / accel.X : rVelocity.X / accelR.X,
                accel.Y + rVelocity.Y < rVelocity.Y - accelR.Y ? rVelocity.Y / accel.Y : rVelocity.Y / accelR.Y,
                accel.Z + rVelocity.Z < rVelocity.Z - accelR.Z ? rVelocity.Z / accel.Z : rVelocity.Z / accelR.Z
                );

            // Distance from projected stop position to center
            return gridCenter - (velocity * timeToStop.Length()) / 2;
        }


        // In and out, 20 minute adventure.
        public void ThrustControl(Vector3D relPos, List<IMyThrust> up, List<IMyThrust> down, List<IMyThrust> left, List<IMyThrust> right, List<IMyThrust> forward, List<IMyThrust> back)
        {
            /* 
             * 0 forward
             * 1 back
             * 2 up
             * 3 down
             * 4 right
             * 5 left
             */

            // Converts relative position to clamped thrust direction
            //Vector3D tMove = pid.Control(relPos);
            Vector3D tMove = relPos;

            //tMove = Vector3D.Clamp(tMove, new Vector3D(-1, -1, -1), new Vector3D(1, 1, 1));


            //Vector3D.Normalize(ref tMove, out tMove);

            // Rotate thrust direction to line up with grid orientation
            MatrixD wm = Me.WorldMatrix;
            tMove = new Vector3D(Vector3D.Dot(tMove, wm.Right), Vector3D.Dot(tMove, wm.Forward), Vector3D.Dot(tMove, wm.Up));

            // Output thrust to thrusters [ ))) ]
            SetThrust(-tMove.X, right, true);
            SetThrust(tMove.X, left, true);
            SetThrust(-tMove.Y, forward, true);
            SetThrust(tMove.Y, back, true);
            SetThrust(-tMove.Z, up, true);
            SetThrust(tMove.Z, down, true);


            // TODO: If speed > 400, don't fire.
            if (hasABs && relPos.LengthSquared() > 10000)
            {
                if (tMove.X > 0.25) foreach (var ab in leftAB) wAPI.FireWeaponOnce(ab);
                if (-tMove.X > 0.25) foreach (var ab in rightAB) wAPI.FireWeaponOnce(ab);
                if (tMove.Y > 0.25) foreach (var ab in forwardAB) wAPI.FireWeaponOnce(ab);
                if (-tMove.Y > 0.25) foreach (var ab in backAB) wAPI.FireWeaponOnce(ab);
                if (tMove.Z > 0.25) foreach (var ab in upAB) wAPI.FireWeaponOnce(ab);
                if (-tMove.Z > 0.25) foreach (var ab in downAB) wAPI.FireWeaponOnce(ab);
            }
        }
        // AAAAAAARRRRRRRRRRRGGGGGGGGGGGGGGGHHHHHHHHHHHHHHHHHHHH
        // 4 hours.
        // Thanks invalid


        public void SetThrust(double pct, List<IMyThrust> thrusters, bool disable)
        {
            float percent = (float)pct;
            if (thrusters.Count == 0) return;
            foreach (var thrust in thrusters)
            {
                //thrust.Enabled = percent > 0 || !disable;
                thrust.ThrustOverridePercentage = percent;
            }
        }

        public void RecalcThrust()
        {
            foreach (var thrust in allThrust)
            {
                /* 
                 * 0 forward
                 * 1 back
                 * 2 up
                 * 3 down
                 * 4 right
                 * 5 left
                 */

                if (thrust.Orientation.Forward == Base6Directions.GetOppositeDirection(Me.Orientation.Forward))
                {
                    forwardThrust.Add(thrust);
                    thrustAmt[0] += thrust.MaxEffectiveThrust;
                    continue;
                }
                if (thrust.Orientation.Forward == Base6Directions.GetOppositeDirection(Me.Orientation.Up))
                {
                    upThrust.Add(thrust);
                    thrustAmt[2] += thrust.MaxEffectiveThrust;
                    continue;
                }
                if (thrust.Orientation.Forward == Base6Directions.GetOppositeDirection(Me.Orientation.Left))
                {
                    leftThrust.Add(thrust);
                    thrustAmt[5] += thrust.MaxEffectiveThrust;
                    continue;
                }

                if (thrust.Orientation.Forward == Me.Orientation.Forward)
                {
                    backThrust.Add(thrust);
                    thrustAmt[1] += thrust.MaxEffectiveThrust;
                    continue;
                }
                if (thrust.Orientation.Forward == Me.Orientation.Up)
                {
                    downThrust.Add(thrust);
                    thrustAmt[3] += thrust.MaxEffectiveThrust;
                    continue;
                }
                if (thrust.Orientation.Forward == Me.Orientation.Left)
                {
                    rightThrust.Add(thrust);
                    thrustAmt[4] += thrust.MaxEffectiveThrust;
                    continue;
                }
            }
        }

        public void RecalcABs()
        {
            // Sort afterburners
            foreach (var ab in allABs)
            {
                if (ab.Orientation.Forward == Base6Directions.GetOppositeDirection(Me.Orientation.Forward))
                {
                    forwardAB.Add(ab);
                }
                if (ab.Orientation.Forward == Base6Directions.GetOppositeDirection(Me.Orientation.Up))
                {
                    upAB.Add(ab);
                }
                if (ab.Orientation.Forward == Base6Directions.GetOppositeDirection(Me.Orientation.Left))
                {
                    leftAB.Add(ab);
                }

                if (ab.Orientation.Forward == Me.Orientation.Forward)
                {
                    backAB.Add(ab);
                }
                if (ab.Orientation.Forward == Me.Orientation.Up)
                {
                    downAB.Add(ab);
                }
                if (ab.Orientation.Forward == Me.Orientation.Left)
                {
                    rightAB.Add(ab);
                }
            }
        }


        public double RoundPlaces(double d, int place)
        {
            return ((int)(d * Math.Pow(10, place))) / Math.Pow(10, place);
        }
        public double TWRCalc(int dir)
        {
            return thrustAmt[dir] / mass;
        }
        public void SendGroupMsg<T>(Object message, bool sendAll) // Shorthand so I don't have to type out like 50 chars every time I do an IGC call
        {
            if (group == 0 || sendAll)
            {
                IGC.SendBroadcastMessage("1", (T)message);
                IGC.SendBroadcastMessage("2", (T)message);
                IGC.SendBroadcastMessage("3", (T)message);
                IGC.SendBroadcastMessage("4", (T)message);
            }
            else
            {
                IGC.SendBroadcastMessage(group.ToString(), (T)message);
            }
        }

        public char IndicateRun()
        {
            switch (runIndicator)
            {
                case '|':
                    runIndicator = '/';
                    break;
                case '/':
                    runIndicator = '-';
                    break;
                case '-':
                    runIndicator = '\\';
                    break;
                case '\\':
                    runIndicator = '|';
                    break;
            }
            return runIndicator;
        }


        #region Whip's Gyro Control

        static IMyGridTerminalSystem gridSystem;
        static long gridId;

        public static T GetBlock<T>(string name, bool useSubgrids = false) where T : class, IMyTerminalBlock
        {
            if (useSubgrids)
            {
                return (T)gridSystem.GetBlockWithName(name);
            }
            else
            {
                List<T> blocks = GetBlocks<T>(false);
                foreach (T block in blocks)
                {
                    if (block.CustomName == name)
                        return block;
                }
                return null;
            }
        }
        public static T GetBlock<T>(bool useSubgrids = false) where T : class, IMyTerminalBlock
        {
            List<T> blocks = GetBlocks<T>(useSubgrids);
            return blocks.FirstOrDefault();
        }
        public static List<T> GetBlocks<T>(string groupName, bool useSubgrids = false) where T : class, IMyTerminalBlock
        {
            if (string.IsNullOrWhiteSpace(groupName))
                return GetBlocks<T>(useSubgrids);

            IMyBlockGroup group = gridSystem.GetBlockGroupWithName(groupName);
            List<T> blocks = new List<T>();
            group.GetBlocksOfType(blocks);
            if (!useSubgrids)
                blocks.RemoveAll(block => block.CubeGrid.EntityId != gridId);
            return blocks;

        }
        public static List<T> GetBlocks<T>(bool useSubgrids = false) where T : class, IMyTerminalBlock
        {
            List<T> blocks = new List<T>();
            gridSystem.GetBlocksOfType(blocks);
            if (!useSubgrids)
                blocks.RemoveAll(block => block.CubeGrid.EntityId != gridId);
            return blocks;
        }
        #endregion
    }
}
