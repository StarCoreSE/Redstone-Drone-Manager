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

        // Set this to the grid's mass (in KG) IF there is no controller (cockpit, remote control) on the grid.
        float mass = 1300000;

        // Toggles if CCRP (autofire fixed guns) runs. Leave this on, I beg you. Default [TRUE]
        bool doCcrp = true;

        // Maximum innacuracy of CCRP in degrees. A lower number = higher accuracy requirement. Default [1]
        double maxOffset = 1;

        // How far drones in formation should be from the controller.
        static int formDistance = 250;

        // How far drones in 'main' mode should orbit from the target.
        int mainDistance = 1000;

        // Name of the terminal group containing the drone's fixed guns.
        string gunGroup = "Main";

        // Name of the terminal group containing the drone's afterburners. Make sure it's balanced!
        string abGroup = "Afterburners";

        // Diameter of the harm zone.
        int zoneDiameter = 12000;

        // Toggles debug mode. Outputs performance, may increase performance cost
        bool debug = false;













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

        #region PID values
        static double kP = 16;
        static double kI = 0;
        static double kD = 32;
        static double lowerBound = -1000;
        static double upperBound = 1000;
        static double timeStep = 1.0 / 60;
        #endregion


        Vector3D centerOfGrid = new Vector3D(); // Me.position
        IMyCockpit cockpit;

        long frame = 0;

        MyDetectedEntityInfo aiTarget = new MyDetectedEntityInfo();

        GyroControl gyros;
        MyIni _ini = new MyIni();

        IMyBroadcastListener myBroadcastListener;
        IMyBroadcastListener positionListener;
        IMyBroadcastListener velocityListener;
        IMyBroadcastListener orientListener;
        IMyBroadcastListener performanceListener;
        IMyBroadcastListener dronePosListener;
        MyIGCMessage bufferMessage;


        int group = 1; // Supports groups 1 through 4.

        List<long> droneEntities = new List<long>();
        IDictionary<long, BoundingBoxD> dronePositions = new Dictionary<long, BoundingBoxD>();

        string outText = ""; // Text buffer to avoid lag:tm:

        #region drone-specific

        long controlID = 0;
        DateTime lastControllerPing = DateTime.Now;

        static double cos45 = Math.Sqrt(2) / 2;

        int formation = 0;
        Vector3D[][] formationPresets = new Vector3D[][] {
                            new Vector3D[] // X
                            {
                                // Max 16

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
                                new Vector3D(-1.5*cos45*formDistance, -1.5*cos45*formDistance, 0)
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
        };

        string damageAmmo = "";
        string healAmmo = "";
        bool healController = false;

        VectorPID pid = new VectorPID(kP, kI, kD, lowerBound, upperBound, timeStep);
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

        Vector3D movement = new Vector3D(); // Velocity
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
        Dictionary<IMyTerminalBlock, bool> fixedGuns = new Dictionary<IMyTerminalBlock, bool>(); // Gun, gunIsFiring
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
            catch
            {
                Echo("!!! If you can see this, you're probably missing something important (i.e. antenna) !!!");
            }

            if (isController)
                SendGroupMsg<String>("stop", true);
        }

        public void Init()
        {
            // Squares zoneDiameter to avoid Vector3D.Distance() calls. Roots are quite unperformant.
            zoneDiameter *= zoneDiameter;

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

            Runtime.UpdateFrequency = UpdateFrequency.Update100;

            // Init Weaponcore API
            wAPI = new WcPbApi();
            canRun = wAPI.Activate(Me);
            if (!canRun) return;

            // Init Defense Shields API
            dAPI = new PbApiWrapper(Me);

            // Init Debug Draw API
            d = new DebugAPI(this);


            // Init Whip's GPS Gyro Control
            gridSystem = GridTerminalSystem;
            gridId = Me.CubeGrid.EntityId;
            gyros = new GyroControl(Me, kP, kI, kD, lowerBound, upperBound, timeStep);

            // Get cockpit for controller
            GridTerminalSystem.GetBlocksOfType<IMyCockpit>(null, b => { cockpit = b; return true; });

            // Gets shield controllers. Also disables autofortify if no shield controllers are found.
            List<IMyTerminalBlock> shieldControllers = new List<IMyTerminalBlock>();
            List<IMyTerminalBlock> shieldModulators = new List<IMyTerminalBlock>();
            bool hasEnhancer = false;
            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(shieldControllers, b => { if (!hasEnhancer) hasEnhancer = b.DefinitionDisplayNameText.Contains("Enhancer"); return b.CustomName.Contains("Shield Controller"); });
            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(shieldModulators, b => { return b.CustomName.Contains("Shield Modulator"); });
            if (autoFortify) autoFortify = hasEnhancer;

            // Check if shield controller exists, set up shortcut actions for Fortify and Integrity.
            try
            {
                shieldController = shieldControllers[0];
                shieldController.GetActions(new List<ITerminalAction>(), b => { if (b.Id == "DS-C_ShieldFortify_Toggle") toggleFort = b; return true; });

                shieldModulator = shieldModulators[0];
                if (autoIntegrity) autoIntegrity = shieldModulator != null;
                shieldModulator.GetActions(new List<ITerminalAction>(), b => { if (b.Id == "DS-M_ModulateReInforceProt_Toggle") toggleIntegrity = b; return true; });
            }
            catch
            {
                Echo("No shield controller!");
            }

            // Autosets mass if ship controller detected
            GridTerminalSystem.GetBlocksOfType<IMyShipController>(null, b => { mass = b.CalculateShipMass().TotalMass; return true; });

            // Set antenna ranges to 25k (save a tiny bit of power)
            GridTerminalSystem.GetBlocksOfType<IMyRadioAntenna>(null, b => { b.Radius = 25000; antenna = b; return true; });

            // Get LCDs with name containing 'Rocketman' and sets them up
            if (isController || debug) GridTerminalSystem.GetBlocksOfType(outLcds, b => b.CustomName.ToLower().Contains("rocketman"));
            foreach (var l in outLcds)
            {
                l.ContentType = ContentType.TEXT_AND_IMAGE;
                if (!l.CustomData.Contains("hudlcd")) l.CustomData = "hudlcd";
            }

            // Get fixed guns
            try
            {
                GridTerminalSystem.GetBlockGroupWithName(gunGroup).GetBlocksOfType<IMyTerminalBlock>(null, b => {
                    if (wAPI.HasCoreWeapon(b))
                    {
                        fixedGuns.Add(b, false);
                        return true;
                    }
                    return false;
                });
            }
            catch
            {
                doCcrp = false;
            }

            try
            {
                if (fixedGuns.Count > 0)
                {
                    string[] splitCustomData = new string[2];
                    IMyTerminalBlock b = fixedGuns.Keys.ToList<IMyTerminalBlock>()[0];
                    if (b.CustomData != "")
                    {
                        splitCustomData = b.CustomData.Split('\n');
                        healAmmo = splitCustomData[0];
                        damageAmmo = splitCustomData[1];

                        Echo($"Set ammo types to:\n    HEAL - {healAmmo}\n    DAMAGE - {damageAmmo}");
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
                Echo("No afterburners detected!");
            }

            // Sort afterburners
            RecalcABs();

            // Get all thrust
            GridTerminalSystem.GetBlocksOfType<IMyThrust>(allThrust);

            // Sort thrust and calculate total thrust per direction
            RecalcThrust();

            // Set gyro and thrust override to 0
            gyros.Reset();
            SetThrust(-1f, allThrust, false);

            // Init IGC (Inter-Grid Communications) listener
            myBroadcastListener = IGC.RegisterBroadcastListener(isController ? "-1" : group.ToString());

            positionListener = IGC.RegisterBroadcastListener("pos" + (multipleControllers ? group.ToString() : ""));

            velocityListener = IGC.RegisterBroadcastListener("vel" + (multipleControllers ? group.ToString() : ""));

            orientListener = IGC.RegisterBroadcastListener("ori" + (multipleControllers ? group.ToString() : ""));

            performanceListener = IGC.RegisterBroadcastListener("per");

            dronePosListener = IGC.RegisterBroadcastListener("dpos");

            antenna.HudText = "Awaiting " + (isController ? "Drones!" : "Controller!");

            if (isController)
            {
                SendGroupMsg<String>("r", true); // Reset drone IDs
                SendGroupMsg<String>("m" + mainDistance, true);
                SendGroupMsg<String>("o" + formDistance, true);
            }

            try
            {
                if (shieldController.CustomData == "1") toggleFort.Apply(shieldController);
            }
            catch { }

            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(madars, b => b.DefinitionDisplayNameText == "MADAR");
            Echo($"Found {madars.Count()} MADARs.");

            // Convert maxOffset from human-readable format to dot product
            maxOffset = Math.Cos(maxOffset / 57.2957795);
            Echo("Set maxOffset to " + maxOffset);

            gridId = Me.CubeGrid.EntityId;
            frame = 0;

            // TODO: Add asteroid detection

            Echo("\nSuccessfully initialized - disregard above message.\n Initialized as a " + (isController ? "controller." : "drone."));
        }


        public void Main(string argument, UpdateType updateSource)
        {
            try
            {
                if (argument.Length > 0) ParseCommands(argument.ToLower(), updateSource);
            }
            catch { }

            if (!canRun) // If unable to init WC api, do not run.
            {
                Echo("UNABLE TO RUN! Make sure Weaponcore is enabled.");
                return;
            }
            else if (!isController || mode == 1 || true) // Controller doesn't need to be running constantly, unless in wingman mode. That was a lie to children. Actually it wasn't. AAAAHHHHHHH.
            {
                Runtime.UpdateFrequency = activated ? UpdateFrequency.Update1 : UpdateFrequency.Update100;
            }

            // If IGC message recieved

            while (myBroadcastListener.HasPendingMessage)
            {
                MyIGCMessage message = myBroadcastListener.AcceptMessage();
                if (message.GetType() == typeof(long) && !isController)
                    controlID = message.As<long>();
                else if (message.GetType() == typeof(Vector3D))
                    ctrlTargetPos = message.As<Vector3D>();
                else
                    ParseCommands(message.Data.ToString(), updateSource);
            }

            while (positionListener.HasPendingMessage)
            {
                controllerPos = positionListener.AcceptMessage().As<Vector3D>();
                if (!activated)
                {
                    activated = true;
                    centerOfGrid = Me.CubeGrid.GetPosition();
                }
            }

            if (mode == 1)
                while (velocityListener.HasPendingMessage)
                    anchorVelocity = velocityListener.AcceptMessage().As<Vector3D>();

            while (orientListener.HasPendingMessage)
                ctrlMatrix = orientListener.AcceptMessage().As<MatrixD>();

            while (performanceListener.HasPendingMessage)
                totalRuntime += performanceListener.AcceptMessage().As<double>();

            while (dronePosListener.HasPendingMessage)
            {
                MyIGCMessage m = dronePosListener.AcceptMessage();
                dronePositions[m.Source] = m.As<BoundingBoxD>();
            }

            // Prevents running multiple times per tick
            if (updateSource == UpdateType.IGC)
                return;
            //d.RemoveAll();

            // Status info
            outText += $"[M{mode} : G{group} : ID{id}] {(activated ? "ACTIVE" : "INACTIVE")} {IndicateRun()}\n\nRocketman Drone Manager\n-------------------------\n{(isController ? $"Controlling {droneEntities.Count} drone(s)" : "Drone Mode")}\n";

            if (id == -1 && !isController) // If ID unset and is not controller, ping controller for ID.
            {
                activated = false;
                IGC.SendBroadcastMessage("-1", "e" + Me.CubeGrid.EntityId);
                return;
            }

            if (activated) // Generic "I'm on!" stuff
            {
                // Share World Axis-Aligned Bounding Box for collision avoidance
                IGC.SendBroadcastMessage("dpos", Me.CubeGrid.WorldAABB);

                // Calculate velocity, because keen is a dum dum
                movement = centerOfGrid - Me.CubeGrid.GetPosition();
                centerOfGrid = Me.CubeGrid.GetPosition();

                aiTarget = wAPI.GetAiFocus(gridId).Value;


                outText += "Velocity " + speed + "\n";

                if (frame % 60 == 0)
                {
                    speed = movement.Length() * 60;

                    // Zone-avoidance system

                    /* 
                     * 0 forward
                     * 1 back
                     * 2 up
                     * 3 down
                     * 4 right
                     * 5 left
                     */


                    // Autofortify system

                    // I'm a bad programmer.
                    // Wait, I have something for that.
                    try
                    {
                        // I'm a good programmer.
                        if (autoFortify && dAPI.GridHasShield(Me.CubeGrid))
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
                    }
                    catch { }

                    // AutoIntegrity System
                    try
                    {
                        if (autoIntegrity && dAPI.GridHasShield(Me.CubeGrid) && !isController)
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
                    }
                    catch { }

                    // Revengance status
                    if (!isController && !(mode == 0 && autoTarget)) // If drone and hasn't already gone ballistic
                    {
                        if (DateTime.Now.Subtract(lastControllerPing) > TimeSpan.FromSeconds(10)) // If hasn't recieved message within 10 seconds
                        {
                            mode = 0;          // Go into freeflight mode
                            autoTarget = true; // Automatically attack the nearest enemy
                            antenna.HudText = new Random().NextDouble() > 0.5 ? "YOU BASTARD!" : "I'LL KILL YOU!"; // Ree at the enemy
                        }
                    }
                }

                if (isController) // If on AND is controller
                {
                    SendGroupMsg<Vector3D>(wAPI.GetAiFocus(gridId).Value.Position, false);
                    if (frame % 20 == 0)
                    {
                        SendGroupMsg<long>(wAPI.GetAiFocus(gridId).Value.EntityId, !multipleControllers || group == 0);

                        if (frame % 240 == 0)
                        {
                            SendGroupMsg<String>("c" + Me.CubeGrid.EntityId, true);
                        }
                    }

                    if ((mode == 1) && frame % 2 == 0)
                    {
                        if (multipleControllers)
                        {
                            if (group != 0)
                            {
                                IGC.SendBroadcastMessage("pos" + group, centerOfGrid);
                                IGC.SendBroadcastMessage("vel" + group, cockpit.GetShipVelocities().LinearVelocity);
                                IGC.SendBroadcastMessage("ori" + group, (cockpit.IsFunctional ? cockpit.WorldMatrix : Me.WorldMatrix));
                            }
                            else
                            {
                                for (int i = 1; i < 5; i++)
                                {
                                    IGC.SendBroadcastMessage("pos" + i, centerOfGrid);
                                    IGC.SendBroadcastMessage("vel" + i, cockpit.GetShipVelocities().LinearVelocity);
                                    IGC.SendBroadcastMessage("ori" + i, (cockpit.IsFunctional ? cockpit.WorldMatrix : Me.WorldMatrix));
                                }
                            }
                        }
                        else
                        {
                            // Transmit position and orientation every other tick
                            IGC.SendBroadcastMessage("pos", centerOfGrid);
                            IGC.SendBroadcastMessage("vel", cockpit.GetShipVelocities().LinearVelocity);
                            IGC.SendBroadcastMessage("ori", (cockpit.IsFunctional ? cockpit.WorldMatrix : Me.WorldMatrix));
                        }
                    }
                }

                else // If on AND is drone
                {
                    if (frame % 2 == 0)
                    {
                        // Gets closest target. AHHHHHHHHHHHHHHHHHHHHHHHHHH.
                        if (!healController && (mode == 0 || autoTarget) && frame % 60 == 0)
                        {
                            long target = 0;
                            double dist2 = 0;

                            Dictionary<MyDetectedEntityInfo, float> targets = new Dictionary<MyDetectedEntityInfo, float>();
                            wAPI.GetSortedThreats(Me, targets);

                            foreach (var targ in targets.Keys) // goofy ahh distance sorter
                            {
                                dist2 = Vector3D.DistanceSquared(targ.Position, centerOfGrid);
                                if (dist2 < distanceTarget || distanceTarget == 0)
                                {
                                    distanceTarget = dist2;
                                    target = targ.EntityId;
                                }
                            }


                            wAPI.SetAiFocus(Me, target);
                        }



                        #region CCRP

                        resultPos = aiTarget.IsEmpty() ? ctrlTargetPos : aiTarget.Position;

                        if (doCcrp)
                        {
                            if (healController)
                            {
                                outText += "Locked on controller\n";

                                bool isLinedUp = Vector3D.Normalize(controllerPos - centerOfGrid).Dot(Me.WorldMatrix.Forward) > maxOffset;

                                foreach (var weapon in fixedGuns.Keys.ToList<IMyTerminalBlock>())
                                {
                                    //d.DrawLine(centerOfGrid, Me.WorldMatrix.Forward * wAPI.GetMaxWeaponRange(weapon, 0) + centerOfGrid, Color.White, 0.5f);

                                    if (isLinedUp)
                                    {
                                        if (!weapon.GetValueBool("WC_Shoot"))
                                        {
                                            wAPI.ToggleWeaponFire(weapon, true, true);
                                            fixedGuns[weapon] = true;
                                        }
                                    }
                                    else if (weapon.GetValueBool("WC_Shoot"))
                                    {
                                        wAPI.ToggleWeaponFire(weapon, false, true);
                                        fixedGuns[weapon] = false;
                                    }
                                }
                            }
                            else if (resultPos != new Vector3D())
                            {
                                outText += "Locked on target " + aiTarget.Name + "\n";

                                Vector3D a = Vector3D.Normalize(predictedTargetPos - centerOfGrid);
                                foreach (var weapon in fixedGuns.Keys.ToList<IMyTerminalBlock>())
                                {
                                    // See variable name
                                    predictedTargetPos = (Vector3D)wAPI.GetPredictedTargetPosition(weapon, aiTarget.EntityId, 0);

                                    // Normalized vector forward from weapon
                                    Vector3D b = weapon.WorldMatrix.Forward;

                                    //d.DrawGPS($"Offset {RoundPlaces(a.Dot(b), 4)}/{RoundPlaces(maxOffset, 4)}\n{(a.Dot(b) > maxOffset)}", predictedTargetPos, Color.Red);
                                    //d.DrawLine(centerOfGrid, Me.WorldMatrix.Forward * wAPI.GetMaxWeaponRange(weapon, 0) + centerOfGrid, Color.White, 0.5f);

                                    // Checks if weapon is aligned, and within range. (Uses DistanceSquared for performance reasons [don't do sqrt, kids])
                                    if (a.Dot(b) > maxOffset && Math.Pow(wAPI.GetMaxWeaponRange(weapon, 0), 2) > distanceTarget)
                                    {
                                        if (!weapon.GetValueBool("WC_Shoot"))
                                        {
                                            wAPI.ToggleWeaponFire(weapon, true, true);
                                            fixedGuns[weapon] = true;
                                        }
                                    }
                                    else
                                        if (weapon.GetValueBool("WC_Shoot"))
                                        {
                                            ////d.PrintHUD("Toggled weapons OFF");
                                            wAPI.ToggleWeaponFire(weapon, false, true);
                                            fixedGuns[weapon] = false;
                                        }
                                }
                            }
                        }
                        else
                        {
                            outText += "CCRP not running! " + (doCcrp ? "NO TARGET" : "DISABLED") + "\n";
                        }
                        #endregion

                        bool needsRecalc = false;
                        // check if any thrusters are dead
                        foreach (var t in allThrust)
                            if (t.WorldAABB == new BoundingBoxD())
                            {
                                needsRecalc = true;
                                break;
                            }
                        if (needsRecalc)
                        {
                            GridTerminalSystem.GetBlocksOfType<IMyThrust>(allThrust);
                            RecalcThrust();
                            Echo($"Recalculated thrust with {allThrust.Count} thrusters");
                        }
                    }

                    Vector3D vecToEnemy = doCcrp && !resultPos.IsZero() ? Vector3D.Normalize(resultPos - centerOfGrid) : ctrlMatrix.Forward;
                    Vector3D moveTo = new Vector3D();
                    //vThrust = Vector3D.Rotate(new Vector3D(TWRCalc(4) < TWRCalc(5) ? TWRCalc(4) : TWRCalc(5), TWRCalc(0) < TWRCalc(1) ? TWRCalc(0) : TWRCalc(1), TWRCalc(2) < TWRCalc(3) ? TWRCalc(2) : TWRCalc(3)), Me.CubeGrid.WorldMatrix);

                    Vector3D stopPosition = CalcStopPosition(movement*60, centerOfGrid);
                    //d.DrawLine(centerOfGrid, resultPos, Color.Red, 0.1f);
                    //d.DrawGPS("Stop Position", stopPosition);

                    // Autostop when near zone
                    if (stopPosition.LengthSquared() > zoneDiameter)
                    {
                        //d.PrintHUD("YOU BLOODY IDIOT, YOU MADE ME GO OUT OF THE ZONE");
                        ThrustControl(centerOfGrid, upThrust, downThrust, leftThrust, rightThrust, forwardThrust, backThrust);
                    }

                    else
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

                                ThrustControl(stopPosition - moveTo, upThrust, downThrust, leftThrust, rightThrust, forwardThrust, backThrust);
                                break;

                            case 1: // Orbit Controller

                                double dSq = Vector3D.DistanceSquared(controllerPos, Me.CubeGrid.GetPosition());

                                if (!healController && damageAmmo != "") {
                                    //d.PrintHUD($"Damage ammo {damageAmmo}");
                                    foreach (var wep in fixedGuns.Keys)
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

                                ThrustControl(stopPosition - moveTo, upThrust, downThrust, leftThrust, rightThrust, forwardThrust, backThrust);

                                break;

                            case 2: // Sit and Fortify

                                gyros.FaceVectors(vecToEnemy, Me.WorldMatrix.Up);

                                moveTo = (controllerPos + formationPresets[formation][id]);

                                closestCollision = CheckCollision(moveTo);

                                if (closestCollision != new Vector3D())
                                    moveTo += moveTo.Cross(closestCollision);

                                ThrustControl(stopPosition - moveTo, upThrust, downThrust, leftThrust, rightThrust, forwardThrust, backThrust);

                                break;
                        }
                }

            }

            if (debug) // Print runtime info
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
            foreach (var l in outLcds)
            {
                l.WriteText(outText);
            }
            outText = "";

            frame++;
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
                    return;
                case "wing":
                    mode = 1;
                    healController = false;
                    if (isController)
                        SendGroupMsg<String>("wing", false);

                    return;
                case "fort":
                    mode = 2;
                    healController = false;
                    if (isController)
                    {
                        SendGroupMsg<String>("fort", false);
                        IGC.SendBroadcastMessage("pos", centerOfGrid);
                        IGC.SendBroadcastMessage("ori", (cockpit.IsFunctional ? cockpit.WorldMatrix : Me.WorldMatrix));
                    }
                    return;
                case "start":
                    activated = true;
                    centerOfGrid = Me.CubeGrid.GetPosition();
                    if (isController)
                    {
                        if (updateSource == UpdateType.Terminal)
                        {
                            SendGroupMsg<String>("start", true);
                            IGC.SendBroadcastMessage("-1", "start");
                        }
                        IGC.SendBroadcastMessage("pos", centerOfGrid);
                        IGC.SendBroadcastMessage("ori", (cockpit.IsFunctional ? cockpit.WorldMatrix : Me.WorldMatrix));
                    }
                    return;
                case "group":
                    if (group < 4) group++; else group = 0;
                    return;
                case "stop":
                    activated = false;
                    SetThrust(-1f, allThrust, false);
                    gyros.Reset();
                    if (isController)
                    {
                        if (updateSource == UpdateType.Terminal)
                        {
                            SendGroupMsg<String>("stop", true);
                            IGC.SendBroadcastMessage("-1", "stop");
                        }
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
                        foreach (var wep in fixedGuns.Keys)
                        {
                            wep.SetValue<Int64>("WC_PickAmmo", 1);
                        }
                    }
                    return;
                case "learnheal":
                    foreach (var w in fixedGuns.Keys)
                    {
                        healAmmo = wAPI.GetActiveAmmo(w, 0);
                        if (damageAmmo != "") w.CustomData = healAmmo + "\n" + damageAmmo;
                    }
                    Echo("Learned damage ammo " + healAmmo);
                    break;
                case "learndamage":
                    foreach (var w in fixedGuns.Keys)
                    {
                        damageAmmo = wAPI.GetActiveAmmo(w, 0);
                        if (healAmmo != "") w.CustomData = healAmmo + "\n" + damageAmmo;
                    }
                    Echo("Learned damage ammo " + damageAmmo);
                    break;
                case "ctrlgroup":
                    if (isController)
                    {
                        SendGroupMsg<String>("c" + Me.CubeGrid.EntityId, false);
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
            else lastControllerPing = DateTime.Now;

            antenna.HudText = id.ToString() + " | " + mode.ToString() + group.ToString();

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
                    if (!isController && long.Parse(argument.Substring(3)) == Me.CubeGrid.EntityId) id = int.Parse(argument.Substring(1, 2));
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
                default:
                    long targetID = 0;
                    if (mode != 0 && !autoTarget && long.TryParse(argument, out targetID))
                    {
                        wAPI.SetAiFocus(Me, targetID);
                    }
                    break;
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
            return (thrustAmt[dir]) / mass;
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

        public class VectorPID
        {
            private PID X;
            private PID Y;
            private PID Z;

            public VectorPID(double kP, double kI, double kD, double lowerBound, double upperBound, double timeStep)
            {
                X = new PID(kP, kI, kD, lowerBound, upperBound, timeStep);
                Y = new PID(kP, kI, kD, lowerBound, upperBound, timeStep);
                Z = new PID(kP, kI, kD, lowerBound, upperBound, timeStep);
            }

            public VectorPID(double kP, double kI, double kD, double integralDecayRatio, double timeStep)
            {
                X = new PID(kP, kI, kD, integralDecayRatio, timeStep);
                Y = new PID(kP, kI, kD, integralDecayRatio, timeStep);
                Z = new PID(kP, kI, kD, integralDecayRatio, timeStep);
            }

            public Vector3D Control(Vector3D error)
            {
                return new Vector3D(X.Control(error.X), Y.Control(error.Y), Z.Control(error.Z));
            }

            public void Reset()
            {
                X.Reset();
                Y.Reset();
                Z.Reset();
            }
        }

        public class GyroControl
        {
            private List<IMyGyro> gyros;
            IMyTerminalBlock rc;

            public GyroControl(IMyTerminalBlock rc, double kP, double kI, double kD, double lowerBound, double upperBound, double timeStep)
            {
                this.rc = rc;

                gyros = GetBlocks<IMyGyro>();

                anglePID = new VectorPID(kP, kI, kD, lowerBound, upperBound, timeStep);

                Reset();
            }
            // In (pitch, yaw, roll)
            VectorPID anglePID;

            public void Reset()
            {
                for (int i = 0; i < gyros.Count; i++)
                {
                    IMyGyro g = gyros[i];
                    if (g == null)
                    {
                        gyros.RemoveAtFast(i);
                        continue;
                    }
                    g.GyroOverride = false;
                }
                anglePID.Reset();
            }


            Vector3D GetAngles(MatrixD current, Vector3D forward, Vector3D up)
            {
                Vector3D error = new Vector3D();
                if (forward != Vector3D.Zero)
                {
                    Quaternion quat = Quaternion.CreateFromForwardUp(current.Forward, current.Up);
                    Quaternion invQuat = Quaternion.Inverse(quat);
                    Vector3D RCReferenceFrameVector = Vector3D.Transform(forward, invQuat); //Target Vector In Terms Of RC Block

                    //Convert To Local Azimuth And Elevation
                    Vector3D.GetAzimuthAndElevation(RCReferenceFrameVector, out error.Y, out error.X);
                }

                if (up != Vector3D.Zero)
                {
                    Vector3D temp = Vector3D.Normalize(VectorRejection(up, rc.WorldMatrix.Forward));
                    double dot = MathHelper.Clamp(Vector3D.Dot(rc.WorldMatrix.Up, temp), -1, 1);
                    double rollAngle = Math.Acos(dot);
                    double scaler = ScalerProjection(temp, rc.WorldMatrix.Right);
                    if (scaler > 0)
                        rollAngle *= -1;
                    error.Z = rollAngle;
                }

                if (Math.Abs(error.X) < 0.001)
                    error.X = 0;
                if (Math.Abs(error.Y) < 0.001)
                    error.Y = 0;
                if (Math.Abs(error.Z) < 0.001)
                    error.Z = 0;

                return error;
            }

            public void FaceVectors(Vector3D forward, Vector3D up)
            {
                // In (pitch, yaw, roll)
                Vector3D error = -GetAngles(rc.WorldMatrix, forward, up);
                Vector3D angles = new Vector3D(anglePID.Control(error));
                ApplyGyroOverride(rc.WorldMatrix, angles);
            }
            void ApplyGyroOverride(MatrixD current, Vector3D localAngles)
            {
                Vector3D worldAngles = Vector3D.TransformNormal(localAngles, current);
                foreach (IMyGyro gyro in gyros)
                {
                    Vector3D transVect = Vector3D.TransformNormal(worldAngles, MatrixD.Transpose(gyro.WorldMatrix));  //Converts To Gyro Local
                    if (!transVect.IsValid())
                        throw new Exception("Invalid trans vector. " + transVect.ToString());

                    gyro.Pitch = (float)transVect.X;
                    gyro.Yaw = (float)transVect.Y;
                    gyro.Roll = (float)transVect.Z;
                    gyro.GyroOverride = true;
                }
            }

            /// <summary>
            /// Projects a value onto another vector.
            /// </summary>
            /// <param name="guide">Must be of length 1.</param>
            public static double ScalerProjection(Vector3D value, Vector3D guide)
            {
                double returnValue = Vector3D.Dot(value, guide);
                if (double.IsNaN(returnValue))
                    return 0;
                return returnValue;
            }

            /// <summary>
            /// Projects a value onto another vector.
            /// </summary>
            /// <param name="guide">Must be of length 1.</param>
            public static Vector3D VectorPojection(Vector3D value, Vector3D guide)
            {
                return ScalerProjection(value, guide) * guide;
            }

            /// <summary>
            /// Projects a value onto another vector.
            /// </summary>
            /// <param name="guide">Must be of length 1.</param>
            public static Vector3D VectorRejection(Vector3D value, Vector3D guide)
            {
                return value - VectorPojection(value, guide);
            }
        }

        //Whip's PID controller class v6 - 11/22/17
        public class PID
        {
            double _kP = 0;
            double _kI = 0;
            double _kD = 0;
            double _integralDecayRatio = 0;
            double _lowerBound = 0;
            double _upperBound = 0;
            double _timeStep = 0;
            double _inverseTimeStep = 0;
            double _errorSum = 0;
            double _lastError = 0;
            bool _firstRun = true;
            bool _integralDecay = false;
            public double Value
            {
                get; private set;
            }

            public PID(double kP, double kI, double kD, double lowerBound, double upperBound, double timeStep)
            {
                _kP = kP;
                _kI = kI;
                _kD = kD;
                _lowerBound = lowerBound;
                _upperBound = upperBound;
                _timeStep = timeStep;
                _inverseTimeStep = 1 / _timeStep;
                _integralDecay = false;
            }

            public PID(double kP, double kI, double kD, double integralDecayRatio, double timeStep)
            {
                _kP = kP;
                _kI = kI;
                _kD = kD;
                _timeStep = timeStep;
                _inverseTimeStep = 1 / _timeStep;
                _integralDecayRatio = integralDecayRatio;
                _integralDecay = true;
            }

            public double Control(double error)
            {
                //Compute derivative term
                var errorDerivative = (error - _lastError) * _inverseTimeStep;

                if (_firstRun)
                {
                    errorDerivative = 0;
                    _firstRun = false;
                }

                //Compute integral term
                if (!_integralDecay)
                {
                    _errorSum += error * _timeStep;

                    //Clamp integral term
                    if (_errorSum > _upperBound)
                        _errorSum = _upperBound;
                    else if (_errorSum < _lowerBound)
                        _errorSum = _lowerBound;
                }
                else
                {
                    _errorSum = _errorSum * (1.0 - _integralDecayRatio) + error * _timeStep;
                }

                //Store this error as last error
                _lastError = error;

                //Construct output
                this.Value = _kP * error + _kI * _errorSum + _kD * errorDerivative;
                return this.Value;
            }

            public double Control(double error, double timeStep)
            {
                _timeStep = timeStep;
                _inverseTimeStep = 1 / _timeStep;
                return Control(error);
            }

            public void Reset()
            {
                _errorSum = 0;
                _lastError = 0;
                _firstRun = true;
            }
        }

        static IMyGridTerminalSystem gridSystem;
        static long gridId;

        static T GetBlock<T>(string name, bool useSubgrids = false) where T : class, IMyTerminalBlock
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
        static T GetBlock<T>(bool useSubgrids = false) where T : class, IMyTerminalBlock
        {
            List<T> blocks = GetBlocks<T>(useSubgrids);
            return blocks.FirstOrDefault();
        }
        static List<T> GetBlocks<T>(string groupName, bool useSubgrids = false) where T : class, IMyTerminalBlock
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
        static List<T> GetBlocks<T>(bool useSubgrids = false) where T : class, IMyTerminalBlock
        {
            List<T> blocks = new List<T>();
            gridSystem.GetBlocksOfType(blocks);
            if (!useSubgrids)
                blocks.RemoveAll(block => block.CubeGrid.EntityId != gridId);
            return blocks;
        }
        #endregion

        #region DebugDraw

        public class DebugAPI
        {
            public readonly bool ModDetected;

            public void RemoveDraw() => _removeDraw?.Invoke(_pb);
            Action<IMyProgrammableBlock> _removeDraw;

            public void RemoveAll() => _removeAll?.Invoke(_pb);
            Action<IMyProgrammableBlock> _removeAll;

            public void Remove(int id) => _remove?.Invoke(_pb, id);
            Action<IMyProgrammableBlock, int> _remove;

            public int DrawPoint(Vector3D origin, Color color, float radius = 0.2f, float seconds = DefaultSeconds, bool? onTop = null) => _point?.Invoke(_pb, origin, color, radius, seconds, onTop ?? _defaultOnTop) ?? -1;
            Func<IMyProgrammableBlock, Vector3D, Color, float, float, bool, int> _point;

            public int DrawLine(Vector3D start, Vector3D end, Color color, float thickness = DefaultThickness, float seconds = DefaultSeconds, bool? onTop = null) => _line?.Invoke(_pb, start, end, color, thickness, seconds, onTop ?? _defaultOnTop) ?? -1;
            Func<IMyProgrammableBlock, Vector3D, Vector3D, Color, float, float, bool, int> _line;

            public int DrawAABB(BoundingBoxD bb, Color color, Style style = Style.Wireframe, float thickness = DefaultThickness, float seconds = DefaultSeconds, bool? onTop = null) => _aabb?.Invoke(_pb, bb, color, (int)style, thickness, seconds, onTop ?? _defaultOnTop) ?? -1;
            Func<IMyProgrammableBlock, BoundingBoxD, Color, int, float, float, bool, int> _aabb;

            public int DrawOBB(MyOrientedBoundingBoxD obb, Color color, Style style = Style.Wireframe, float thickness = DefaultThickness, float seconds = DefaultSeconds, bool? onTop = null) => _obb?.Invoke(_pb, obb, color, (int)style, thickness, seconds, onTop ?? _defaultOnTop) ?? -1;
            Func<IMyProgrammableBlock, MyOrientedBoundingBoxD, Color, int, float, float, bool, int> _obb;

            public int DrawSphere(BoundingSphereD sphere, Color color, Style style = Style.Wireframe, float thickness = DefaultThickness, int lineEveryDegrees = 15, float seconds = DefaultSeconds, bool? onTop = null) => _sphere?.Invoke(_pb, sphere, color, (int)style, thickness, lineEveryDegrees, seconds, onTop ?? _defaultOnTop) ?? -1;
            Func<IMyProgrammableBlock, BoundingSphereD, Color, int, float, int, float, bool, int> _sphere;

            public int DrawMatrix(MatrixD matrix, float length = 1f, float thickness = DefaultThickness, float seconds = DefaultSeconds, bool? onTop = null) => _matrix?.Invoke(_pb, matrix, length, thickness, seconds, onTop ?? _defaultOnTop) ?? -1;
            Func<IMyProgrammableBlock, MatrixD, float, float, float, bool, int> _matrix;

            public int DrawGPS(string name, Vector3D origin, Color? color = null, float seconds = DefaultSeconds) => _gps?.Invoke(_pb, name, origin, color, seconds) ?? -1;
            Func<IMyProgrammableBlock, string, Vector3D, Color?, float, int> _gps;

            public int PrintHUD(string message, Font font = Font.Debug, float seconds = 2) => _printHUD?.Invoke(_pb, message, font.ToString(), seconds) ?? -1;
            Func<IMyProgrammableBlock, string, string, float, int> _printHUD;

            public void PrintChat(string message, string sender = null, Color? senderColor = null, Font font = Font.Debug) => _chat?.Invoke(_pb, message, sender, senderColor, font.ToString());
            Action<IMyProgrammableBlock, string, string, Color?, string> _chat;

            public void DeclareAdjustNumber(out int id, double initial, double step = 0.05, Input modifier = Input.Control, string label = null) => id = _adjustNumber?.Invoke(_pb, initial, step, modifier.ToString(), label) ?? -1;
            Func<IMyProgrammableBlock, double, double, string, string, int> _adjustNumber;

            public double GetAdjustNumber(int id, double noModDefault = 1) => _getAdjustNumber?.Invoke(_pb, id) ?? noModDefault;
            Func<IMyProgrammableBlock, int, double> _getAdjustNumber;

            public int GetTick() => _tick?.Invoke() ?? -1;
            Func<int> _tick;

            public enum Style { Solid, Wireframe, SolidAndWireframe }
            public enum Input { MouseLeftButton, MouseRightButton, MouseMiddleButton, MouseExtraButton1, MouseExtraButton2, LeftShift, RightShift, LeftControl, RightControl, LeftAlt, RightAlt, Tab, Shift, Control, Alt, Space, PageUp, PageDown, End, Home, Insert, Delete, Left, Up, Right, Down, D0, D1, D2, D3, D4, D5, D6, D7, D8, D9, A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z, NumPad0, NumPad1, NumPad2, NumPad3, NumPad4, NumPad5, NumPad6, NumPad7, NumPad8, NumPad9, Multiply, Add, Separator, Subtract, Decimal, Divide, F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12 }
            public enum Font { Debug, White, Red, Green, Blue, DarkBlue }

            const float DefaultThickness = 0.02f;
            const float DefaultSeconds = -1;

            IMyProgrammableBlock _pb;
            bool _defaultOnTop;

            public DebugAPI(MyGridProgram program, bool drawOnTopDefault = false)
            {
                if (program == null)
                    throw new Exception("Pass `this` into the API, not null.");

                _defaultOnTop = drawOnTopDefault;
                _pb = program.Me;

                var methods = _pb.GetProperty("DebugAPI")?.As<IReadOnlyDictionary<string, Delegate>>()?.GetValue(_pb);
                if (methods != null)
                {
                    Assign(out _removeAll, methods["RemoveAll"]);
                    Assign(out _removeDraw, methods["RemoveDraw"]);
                    Assign(out _remove, methods["Remove"]);
                    Assign(out _point, methods["Point"]);
                    Assign(out _line, methods["Line"]);
                    Assign(out _aabb, methods["AABB"]);
                    Assign(out _obb, methods["OBB"]);
                    Assign(out _sphere, methods["Sphere"]);
                    Assign(out _matrix, methods["Matrix"]);
                    Assign(out _gps, methods["GPS"]);
                    Assign(out _printHUD, methods["HUDNotification"]);
                    Assign(out _chat, methods["Chat"]);
                    Assign(out _adjustNumber, methods["DeclareAdjustNumber"]);
                    Assign(out _getAdjustNumber, methods["GetAdjustNumber"]);
                    Assign(out _tick, methods["Tick"]);
                    RemoveAll();
                    ModDetected = true;
                }
            }

            void Assign<T>(out T field, object method) => field = (T)method;
        }
        #endregion
    }
}
