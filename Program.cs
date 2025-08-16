using CoreSystems.Api;
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

        // Updated 11/14/23 //


        /* CONFIG */

        /* GENERAL SETTINGS */

        // If this PB is on a drone, set to 'false'. If this PB is on a ship, set to 'true'.
        bool isController = false;

        // Set this to true if multiple controllers are in use
        bool multipleControllers = false;

        // If speed <12 m/s, fortify and shieldfit 0. Else unfortify. Default [true]
        bool autoFortify = true;

        // If enemy <100m, enable structural integrity. Else unset integrity. Default [true]
        bool autoIntegrity = true;

        // If true, find own target. If false, use controller target. Default [false]
        bool autoTarget = false;

        // If true, rotate around controller grid. If false, remain fixed. Only applies to controller. Default [true]
        bool rotate = true;

        // Speed of rotation around parent grid. Higher = slower. Default [6]
        float orbitsPerSecond = 6;

        // How far (in meters) drones in formation should be from the controller. Default [250]
        static int formDistance = 250;

        // How far (in meters) drones in 'main' mode should orbit from the target. Default [1000]
        int mainDistance = 1000;

        // Toggles debug mode. Outputs performance, but increases performance cost. Default [250]
        bool debug = true;

        /* PERFORMANCE SETTINGS */

        // Runtime threshold in milliseconds - operations throttle above this
        double runtimeThreshold = 0.8;

        // Exponential moving average significance for runtime tracking
        const double RUNTIME_SIGNIFICANCE = 0.005;


        /* DRONE SETTINGS */

        // Set this to the grid's mass (in KG) IF there is no controller (cockpit, remote control) on the grid.
        // put one of these on the fucking drone or i'll fucking show up in your room while you're asleep and steal shit -thecrystalwoods
        float mass = 1300000;

        // Toggles if CCRP (autofire fixed guns) runs. Leave this on, I beg you. Default [TRUE]
        bool doCcrp = true;

        // Maximum innacuracy of CCRP in degrees. A lower number = higher accuracy requirement. Default [2]
        double maxOffset = 2;

        // Name of the terminal group containing the drone's fixed guns.
        string gunGroupName = "Main";

        // Name of the terminal group containing the drone's afterburners. Make sure it's balanced!
        string abGroupName = "Afterburners";

        // Name of the terminal group containing the drone's flares. Currently only supports single-use flares.
        string flareGroupName = "Flares";

        // Minimum number of missiles targeting this grid before autoflare triggers.
        int missilesToFlare = 4;

        // Minimum age in seconds of oldest missile before autoflare triggers.
        int minMissileAge = 2;

        // If true, remain within [zoneRadius] of world origin. If false, remain within [zoneRadius] of controller. Default [true]
        bool fixedFlightArea = false;

        // Radius of the harm zone. this is in meters, not km.
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

        string[] angryText = new string[]
        {
            "ANGERY",
            "YOU SHOULD FLY INTO THE HARMZONE... NOW!",
            "*Pumped Up Kicks*",
            "R2D2 noises",
            "IT'S JOEVER",
            "Healing Spartan...",
            "THE FEDS ARE HERE",
            "controller died of ligma",
            "Large Grid 80085",
            "aristeas says hi",
            "PMW mode enabled",
            "Call an ambulance! Call an ambulance!",
            "yummy blocker plates",
            "WHO TOUCHED MY GUN",
            "send nukes",
            "Anomaly would be proud",
            "zone-chan is the best waifu"
        };


        // DON'T EDIT BELOW THIS LINE UNLESS YOU REALLY KNOW WHAT YOU'RE DOING //
        // OR YOU'RE ARISTEAS //
        //or you're oat :P//
        // I CAN'T STOP MYSELF //

        #endregion

        // In Development Version //
        //now with 2!!! contributers!!!!1!11!1!!!111!//
        // holy hell //

        int mode = 1;
        /*
         * 0 - Main
         *     Shoot and Scoot
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
        TargetingHelper targeting;

        DebugAPI d;

        double averageRuntimeMS = 0;

        bool activated = false; // have I been told to move?

        Vector3D centerOfGrid = new Vector3D(); // Me.position
        IMyCockpit cockpit;

        long frame = 0;

        MyDetectedEntityInfo aiTarget = new MyDetectedEntityInfo();

        GyroControl gyros;

        IMyBroadcastListener myBroadcastListener;
        IMyBroadcastListener positionListener;
        IMyBroadcastListener velocityListener;
        IMyBroadcastListener orientListener;
        IMyBroadcastListener performanceListener;


        int group = 1; // Supports groups 1 through 4.

        List<long> droneEntities = new List<long>();
        List<MyDetectedEntityInfo> friendlies = new List<MyDetectedEntityInfo>();

        MyTuple<bool, int, int> projectilesLockedOn = new MyTuple<bool, int, int>(false, 0, -1);

        public string outText = ""; // Text buffer to avoid lag:tm:

        public long gridId = 0;

        #region drone-specific

        long controlID = 0;
        long lastControllerPing = 0;

        static readonly double cos45 = Math.Sqrt(2) / 2;
        int ozoneRadius;

        int formation = 0;

        readonly Vector3D[][] formationPresets = new Vector3D[][]
        {
            new Vector3D[] // X
            {
                // Max 32

                // Ring 1
                new Vector3D(formDistance, 0, 0),
                new Vector3D(-formDistance, 0, 0),
                new Vector3D(0, formDistance, 0),
                new Vector3D(0, -formDistance, 0),
                new Vector3D(cos45 * formDistance, cos45 * formDistance, 0),
                new Vector3D(cos45 * formDistance, -cos45 * formDistance, 0),
                new Vector3D(-cos45 * formDistance, cos45 * formDistance, 0),
                new Vector3D(-cos45 * formDistance, -cos45 * formDistance, 0),

                // Ring 2
                new Vector3D(1.5 * formDistance, 0, 0),
                new Vector3D(-1.5 * formDistance, 0, 0),
                new Vector3D(0, 1.5 * formDistance, 0),
                new Vector3D(0, -1.5 * formDistance, 0),
                new Vector3D(1.5 * cos45 * formDistance, 1.5 * cos45 * formDistance, 0),
                new Vector3D(1.5 * cos45 * formDistance, -1.5 * cos45 * formDistance, 0),
                new Vector3D(-1.5 * cos45 * formDistance, 1.5 * cos45 * formDistance, 0),
                new Vector3D(-1.5 * cos45 * formDistance, -1.5 * cos45 * formDistance, 0),

                // Ring 3
                new Vector3D(3.0 * formDistance, 0, 0),
                new Vector3D(-3.0 * formDistance, 0, 0),
                new Vector3D(0, 3.0 * formDistance, 0),
                new Vector3D(0, -3.0 * formDistance, 0),
                new Vector3D(3.0 * cos45 * formDistance, 3.0 * cos45 * formDistance, 0),
                new Vector3D(3.0 * cos45 * formDistance, -3.0 * cos45 * formDistance, 0),
                new Vector3D(-3.0 * cos45 * formDistance, 3.0 * cos45 * formDistance, 0),
                new Vector3D(-3.0 * cos45 * formDistance, -3.0 * cos45 * formDistance, 0),

                // Ring 4
                new Vector3D(4.5 * formDistance, 0, 0),
                new Vector3D(-4.5 * formDistance, 0, 0),
                new Vector3D(0, 4.5 * formDistance, 0),
                new Vector3D(0, -4.5 * formDistance, 0),
                new Vector3D(4.5 * cos45 * formDistance, 4.5 * cos45 * formDistance, 0),
                new Vector3D(4.5 * cos45 * formDistance, -4.5 * cos45 * formDistance, 0),
                new Vector3D(-4.5 * cos45 * formDistance, 4.5 * cos45 * formDistance, 0),
                new Vector3D(-4.5 * cos45 * formDistance, -4.5 * cos45 * formDistance, 0)
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

                new Vector3D(cos45 * formDistance, cos45 * formDistance, 0),
                new Vector3D(cos45 * formDistance, -cos45 * formDistance, 0),
                new Vector3D(-cos45 * formDistance, cos45 * formDistance, 0),
                new Vector3D(-cos45 * formDistance, -cos45 * formDistance, 0),
                new Vector3D(0, cos45 * formDistance, cos45 * formDistance),
                new Vector3D(0, cos45 * formDistance, -cos45 * formDistance),
                new Vector3D(0, -cos45 * formDistance, cos45 * formDistance),
                new Vector3D(0, -cos45 * formDistance, -cos45 * formDistance)
            },
            new Vector3D[] // V
            {
                new Vector3D(formDistance / 2, 0, -formDistance / 2),
                new Vector3D(-formDistance / 2, 0, -formDistance / 2),
                new Vector3D(0, formDistance / 2, -formDistance / 2),
                new Vector3D(0, -formDistance / 2, -formDistance / 2),
                new Vector3D(formDistance, 0, 0),
                new Vector3D(-formDistance, 0, 0),
                new Vector3D(0, formDistance, 0),
                new Vector3D(0, -formDistance, 0),
            },
            new Vector3D[] // duckling formation
            {
                new Vector3D(0, 0, formDistance),
                new Vector3D(0, 0, 2 * formDistance),
                new Vector3D(0, 0, 3 * formDistance),
                new Vector3D(0, 0, 4 * formDistance),
                new Vector3D(0, 0, 5 * formDistance),
                new Vector3D(0, 0, 6 * formDistance),
                new Vector3D(0, 0, 7 * formDistance),
                new Vector3D(0, 0, 2 * formDistance),
            },
        };

        string damageAmmo = "";
        string healAmmo = "";
        bool healMode = false;

        MatrixD ctrlMatrix = new MatrixD();
        bool isFortified = false;
        bool isIntegrity = false;
        ITerminalAction toggleFort = null;
        ITerminalAction toggleIntegrity = null;
        Vector3D predictedTargetPos = new Vector3D(); // target position + lead
        Vector3D controllerPos = new Vector3D(); // center of controller ship

        Vector3D
            anchorVelocity =
                new Vector3D(); // velocity of "anchored" target; controller if in WINGMAN mode, target if in MAIN mode.

        Vector3D closestCollision = new Vector3D();
        Vector3D ctrlTargetPos = new Vector3D();
        double totalRuntime = 0;
        Vector3D resultPos = new Vector3D();
        double distanceTarget = 0;
        bool hasABs = false;

        double speed = 0;
        int id = -1; // Per-drone ID. Used for formation flight. Controller is always -1

        double totalSwarmRuntime = 0; // Sum of all drone runtimes this frame
        int swarmDroneCount = 0; // Number of drones that reported performance this frame
        double averageSwarmRuntimeMS = 0; // Rolling average of swarm performance
        List<double> currentFrameRuntimes = new List<double>(); // Store runtimes for current frame

        Dictionary<long, double> dronePerformanceData = new Dictionary<long, double>();
        Dictionary<long, long> droneLastUpdate = new Dictionary<long, long>();
        const int PERFORMANCE_TIMEOUT_FRAMES = 300; // 5 seconds at 60fps
        double lastSwarmCalculation = 0;
        int performanceCalculationFrame = 0;

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
        List<IMyTerminalBlock> flares = new List<IMyTerminalBlock>();
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

        Dictionary<string, int> weaponMap = new Dictionary<string, int>();

        Dictionary<IMyTerminalBlock, Dictionary<string, int>> cachedWeaponMaps =
            new Dictionary<IMyTerminalBlock, Dictionary<string, int>>();

        int weaponMapUpdateFrame = 0;
        int targetUpdateTimer = 0;
        const int TARGET_UPDATE_INTERVAL = 10; // Update every 10 ticks
        bool hasShield = false;
        int shieldCheckFrame = 0;

        Vector3D cachedPredictedPos = new Vector3D();
        int predictedPosUpdateFrame = 0;

        Dictionary<string, Vector3D> cachedAmmoLeadPositions = new Dictionary<string, Vector3D>();
        int ammoLeadUpdateFrame = 0;
        string cachedPrimaryAmmo = "";

        bool pendingRecovery = false;

        #endregion


        public Program()
        {
            TryInit();

            if (isController)
                SendGroupMsg<string>("stop", true);
        }

        public void Save()
        {
            frame = 0;
        }

        public void TryInit()
        {
            try
            {
                Init();
            }
            catch (Exception e)
            {
                Echo(" !!! If you can see this, you're missing something important !!!");
                throw e;
            }
        }

        void Init()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update100;

            // Init Targeting class
            if (TargetingHelper.WcPbApiExists(Me))
                targeting = new WCTargetingHelper(this);
            else
                targeting = new VanillaTargetingHelper(this);
            Echo("Initialized Targeting");

            // Init Defense Shields API
            dAPI = new PbApiWrapper(Me);
            Echo("Initialized dsAPI");

            // Init Debug Draw API

            d = new DebugAPI(this);
            Echo("Initialized debugAPI");


            // Squares zoneRadius to avoid Vector3D.Distance() calls. Roots are quite unperformant.
            zoneRadius *= zoneRadius;
            ozoneRadius = (int)Math.Sqrt(zoneRadius * 0.95);

            // Force clear all WC-related caches that might be from a copied drone
            weaponMap.Clear();
            cachedWeaponMaps.Clear();
            cachedAmmoLeadPositions.Clear();
            cachedPredictedPos = new Vector3D();
            predictedTargetPos = new Vector3D();
            aiTarget = new MyDetectedEntityInfo();

            if (!isController)
            {
                // Check for existing group config
                if (Me.CustomData == "" || Me.CustomData.Length > 2) Me.CustomData = "1";
                int.TryParse(Me.CustomData, out group);
                if (group == -1)
                {
                    isController =
                        true; // IK this looks dumb. Goal is to make sure isController doesn't get accidentally set to false.
                    group = 0;
                }
            }
            else Me.CustomData = "-1";

            Echo($"Checked customdata for group ({group})");

            // Init Whip's GPS Gyro Control
            gridId = Me.CubeGrid.EntityId;
            gyros = new GyroControl(this, Me, kP, kI, kD, lowerBound, upperBound, timeStep);
            Echo("Initialized Whip's Gyro Control");

            // Get cockpit for controller
            GridTerminalSystem.GetBlocksOfType<IMyCockpit>(null, b =>
            {
                cockpit = b;
                return true;
            });
            Echo("Searched for cockpit " + (cockpit == null ? "null" : cockpit.CustomName));

            // Gets shield controllers. Also disables autofortify if no shield controllers are found.
            List<IMyTerminalBlock> shieldControllers = new List<IMyTerminalBlock>();
            List<IMyTerminalBlock> shieldModulators = new List<IMyTerminalBlock>();
            bool hasEnhancer = false;
            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(shieldControllers, b =>
            {
                if (!hasEnhancer) hasEnhancer = b.DefinitionDisplayNameText.Contains("Enhancer");
                return b.CustomName.Contains("Shield Controller");
            });
            Echo($"Located {shieldControllers.Count} shield controllers");
            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(shieldModulators,
                b => { return b.CustomName.Contains("Shield Modulator"); });
            Echo($"Located {shieldModulators.Count} shield modulators");
            if (autoFortify) autoFortify = hasEnhancer;

            // Check if shield controller exists, set up shortcut actions for Fortify and Integrity.
            try
            {
                shieldController = shieldControllers[0];
                shieldController.GetActions(new List<ITerminalAction>(), b =>
                {
                    if (b.Id == "DS-C_ShieldFortify_Toggle") toggleFort = b;
                    return true;
                });
                Echo("Found ShieldFortify action");
            }
            catch
            {
                Echo("No shield controller!");
            }

            // Check if shield controller exists, set up shortcut actions for Fortify and Integrity.
            try
            {
                shieldModulator = shieldModulators[0];
                if (autoIntegrity) autoIntegrity = shieldModulator != null;
                shieldModulator.GetActions(new List<ITerminalAction>(), b =>
                {
                    if (b.Id == "DS-M_ModulateReInforceProt_Toggle") toggleIntegrity = b;
                    return true;
                });
                Echo("Found ModulateReInforceProt action");
            }
            catch
            {
                Echo("No shield modulator!");
            }

            // Autosets mass if ship controller detected
            GridTerminalSystem.GetBlocksOfType<IMyShipController>(null, b =>
            {
                mass = b.CalculateShipMass().TotalMass;
                return true;
            });
            Echo("Set grid mass to " + mass);

            // Set antenna ranges to 25k (save a tiny bit of power)
            GridTerminalSystem.GetBlocksOfType<IMyRadioAntenna>(null, b =>
            {
                b.Radius = 25000;
                antenna = b;
                return true;
            });
            Echo("Set antenna radii to 25km");

            // Get LCDs with name containing 'Rocketman' and sets them up
            if (isController || debug)
                GridTerminalSystem.GetBlocksOfType(outLcds, b => b.CustomName.ToLower().Contains("rocketman"));

            foreach (var l in outLcds)
            {
                l.ContentType = ContentType.TEXT_AND_IMAGE;
                if (!l.CustomData.Contains("hudlcd")) l.CustomData = "hudlcd";
            }

            Echo($"Found {outLcds.Count} LCDs");

            // Get fixed guns
            try
            {
                targeting.GetWeapons(fixedGuns, gunGroupName);
                if (fixedGuns.Count > 0 && targeting is WCTargetingHelper)
                {
                    WCTargetingHelper wcTargeting = (WCTargetingHelper)targeting;
                    wcTargeting.wAPI.GetBlockWeaponMap(fixedGuns.First(), weaponMap);
                }
            }
            catch
            {
                doCcrp = false;
            }

            Echo($"Found {fixedGuns.Count} fixed weapons");

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

                        Echo(
                            $"Set ammo types to:\n    HEAL - {healAmmo}\n    DAMAGE - {damageAmmo}");
                    }
                }
            }
            catch
            {
            }

            // Get afterburners
            try
            {
                targeting.GetWeapons(allABs, abGroupName);
            }
            catch
            {
                Echo("No afterburners detected!");
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

            antenna.HudText = "Awaiting " + (isController ? "Drones!" : "Controller!");

            if (isController)
            {
                SendGroupMsg<string>("r", true); // Reset drone IDs
                SendGroupMsg<string>("m" + mainDistance, true);
                SendGroupMsg<string>("o" + formDistance, true);
                Echo("Reset drone IDs and shared formation distances");
            }

            try
            {
                if (autoFortify && shieldController.CustomData == "1") toggleFort.Apply(shieldController);
            }
            catch
            {
            }

            // Convert maxOffset from human-readable format to dot product
            maxOffset = Math.Cos(maxOffset / 57.2957795);
            Echo("Set maxOffset to " + maxOffset);

            // Cache flares for later use
            try
            {
                targeting.GetWeapons(flares, flareGroupName);
            }
            catch
            {
                Echo("Missing flare group of name: " + flareGroupName);
            }

            minMissileAge *= 60;

            Echo("AutoFlare module initialized\n" + flares.Count + " flares detected");

            // Sets PB surface to text for error reporting.
            // Me.GetSurface(0).ContentType = ContentType.TEXT_AND_IMAGE;

            gridId = Me.CubeGrid.EntityId;
            frame = 0;

            Echo("Successfully initialized as a " + (isController ? "controller." : "drone.") +
                 "");

            // Clear any stale targeting data
            // aiTarget = new MyDetectedEntityInfo();
        }


        int errorCounter = 0;

        public void Main(string argument, UpdateType updateSource)
        {
            // Prevents running multiple times per tick
            if (updateSource == UpdateType.IGC)
                return;

            // Update performance average using EMA
            averageRuntimeMS = RUNTIME_SIGNIFICANCE * Runtime.LastRunTimeMs +
                               (1 - RUNTIME_SIGNIFICANCE) * averageRuntimeMS;

            if (!string.IsNullOrEmpty(argument) &&
                (argument == "recover" || argument == "recycle" || argument == "reset"))
            {
                if (isController)
                {
                    // Broadcast the recover command to all drones
                    SendGroupMsg<string>("recover", true);
                    // Also broadcast to the general channel for any unassigned drones
                    IGC.SendBroadcastMessage("-1", "recover");
                }

                RecoverDrone();
                return;
            }
            else if ((!isController || mode == 1) && !activated)
            {
                Runtime.UpdateFrequency = UpdateFrequency.Update100;
            }


            d.RemoveAll();


            // Update last controller ping
            if (IGCHandler(updateSource))
                lastControllerPing = DateTime.Now.Ticks;

            try
            {
                if (argument.Length > 0) ParseCommands(argument.ToLower(), updateSource);
            }
            catch
            {
            }

            // Status info
            outText +=
                $"M{mode} : G{group} : ID{id} {(activated ? "ACTIVE" : "INACTIVE")} {IndicateRun()}\n";
            outText +=
                $"Runtime: {Runtime.LastRunTimeMs:F2}ms | Avg: {averageRuntimeMS:F2}ms | Limit: {runtimeThreshold}ms\n";
            outText +=
                $"\nRocketman Drone Manager\n-------------------------\n{(isController ? $"Controlling {droneEntities.Count} drone(s)" : "Drone Mode")}\n";

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
                    // ALWAYS do these critical operations regardless of performance
                    centerOfGrid = Me.CubeGrid.GetPosition();

                    // Smart throttling - spread work across frames when performance is poor
                    bool isThrottled = averageRuntimeMS >= runtimeThreshold;
                    int throttleFrames = isThrottled ? 6 : 1; // Spread work across 6 frames when throttled

                    // Frame-distributed operations (only when throttled)
                    if (!isThrottled || (frame % throttleFrames) == 0)
                    {
                        // Only update targeting every 10 ticks (or less frequently if throttled)
                        int targetingInterval = isThrottled ? TARGET_UPDATE_INTERVAL * 3 : TARGET_UPDATE_INTERVAL;
                        if (targetUpdateTimer++ >= targetingInterval)
                        {
                            targeting.Update();
                            targetUpdateTimer = 0;
                        }
                    }

                    if (!isThrottled || (frame % throttleFrames) == 1)
                    {
                        Echo("1");
                        if (!healMode)
                        {
                            var currentTarget = targeting.Target;
                            // Only update aiTarget if we have a valid target from the targeting system
                            if (!currentTarget.IsEmpty())
                            {
                                aiTarget = currentTarget;
                            }
                            // If targeting system has no target but we previously had one, clear it
                            else if (currentTarget.IsEmpty() && !aiTarget.IsEmpty())
                            {
                                aiTarget = new MyDetectedEntityInfo();
                                cachedPredictedPos = new Vector3D();
                                predictedTargetPos = new Vector3D();
                            }
                        }
                    }

                    if (!isThrottled || (frame % throttleFrames) == 2)
                    {
                        outText += "Velocity " + speed + "\n";
                        Echo("2");

                        // Update speed and auto-systems less frequently when throttled
                        int systemUpdateInterval = isThrottled ? 120 : 60;
                        if (frame % systemUpdateInterval == 0)
                        {
                            speed = Me.CubeGrid.LinearVelocity.Length();
                            AutoFortify();
                            AutoIntegrity();
                        }
                    }

                    if (!isThrottled || (frame % throttleFrames) == 3)
                    {
                        Echo("3");
                        if (isController) // If on AND is controller, Update drones with new instructions/positions
                        {
                            Echo("3.5");
                            IGCSendHandler();
                            UpdateSwarmPerformanceAverage();
                        }
                    }

                    // ALWAYS do drone movement - this is critical for safety
                    if (!isController)
                    {
                        Echo("4.-5");
                        RunActiveDrone();
                    }

                    // Add throttling indicator to output
                    if (isThrottled)
                    {
                        outText += $"THROTTLED ({throttleFrames}x slower)\n";
                    }

                    errorCounter = 0;
                    Echo("4");
                }
                // Scary error handling
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

            if (debug)
            {
                PrintDebugText();
                if (averageRuntimeMS < runtimeThreshold * 0.5)
                    DrawLeadDebugs();
            }

            // Check for WeaponCore issues in active drones
            if (activated && !doCcrp && fixedGuns.Count > 0 &&
                frame % (averageRuntimeMS > runtimeThreshold ? 120 : 60) == 0)
            {
                Echo("WeaponCore not active but weapons present - attempting recovery");
                ReinitializeWeaponCore();
            }

            // Auto-detect if we might be a copy with conflicting ID
            if (activated && id != -1 && !isController &&
                frame % (averageRuntimeMS > runtimeThreshold ? 600 : 300) == 0)
            {
                // Check if our stored grid ID doesn't match our actual grid ID
                if (gridId != Me.CubeGrid.EntityId)
                {
                    Echo("Grid ID mismatch detected - likely a copied drone!");
                    Echo($"Stored ID: {gridId}, Actual ID: {Me.CubeGrid.EntityId}");
                    RecoverDrone();
                    return;
                }
            }

            if (activated && !doCcrp && fixedGuns.Count > 0)
            {
                Echo("WeaponCore not active but weapons present - attempting recovery");
                ReinitializeWeaponCore();
            }

            if (averageRuntimeMS < runtimeThreshold)
            {
                foreach (var l in outLcds)
                    l.WriteText(outText);
            }

            outText = "";

            frame++;
        }

        void RecoverDrone()
        {
            Echo("nitiating drone recovery...");

            // Reset core drone state
            id = -1;
            activated = false;
            lastControllerPing = 0;
            aiTarget = new MyDetectedEntityInfo();
            cachedPredictedPos = new Vector3D();
            predictedTargetPos = new Vector3D();
            droneEntities.Clear();
            friendlies.Clear();

            // Reset control systems
            if (gyros != null) gyros.Reset();
            SetThrust(-1f, allThrust, false);

            // Reinitialize WeaponCore
            ReinitializeWeaponCore();

            // Reset grid tracking
            gridId = Me.CubeGrid.EntityId;

            // Reinitialize communication listeners
            myBroadcastListener = IGC.RegisterBroadcastListener(isController ? "-1" : group.ToString());
            positionListener = IGC.RegisterBroadcastListener("pos" + (multipleControllers ? group.ToString() : ""));
            velocityListener = IGC.RegisterBroadcastListener("vel" + (multipleControllers ? group.ToString() : ""));
            orientListener = IGC.RegisterBroadcastListener("ori" + (multipleControllers ? group.ToString() : ""));
            performanceListener = IGC.RegisterBroadcastListener("per");

            // Update status
            if (antenna != null) antenna.HudText = "Recovery Complete - Ready for Combat!";

            // Announce presence to controller (important for copied drones)
            IGC.SendBroadcastMessage("-1", "e" + Me.CubeGrid.EntityId);

            // Reset frame counter
            frame = 0;

            Echo("Drone recovery completed - Grid ID: " + gridId + "");
        }

        void ReinitializeWeaponCore()
        {
            Echo("Reinitializing WeaponCore systems...");

            // Clear existing weapon data
            fixedGuns.Clear();
            allABs.Clear();
            flares.Clear();
            weaponMap.Clear();
            cachedWeaponMaps.Clear();
            cachedAmmoLeadPositions.Clear();

            // Reset WeaponCore state
            doCcrp = true;

            // Reinitialize targeting helper
            try
            {
                if (TargetingHelper.WcPbApiExists(Me))
                    targeting = new WCTargetingHelper(this);
                else
                    targeting = new VanillaTargetingHelper(this);
                Echo("Targeting helper reinitialized");
            }
            catch (Exception e)
            {
                Echo("Failed to reinitialize targeting: " + e.Message);
            }

            // Reinitialize weapons with retry logic
            int retryCount = 0;
            bool wcInitSuccess = false;

            while (retryCount < 3 && !wcInitSuccess)
            {
                try
                {
                    // Get weapons
                    targeting.GetWeapons(fixedGuns, gunGroupName);
                    Echo($"Found {fixedGuns.Count} weapons");

                    // Initialize WeaponCore API if we have weapons
                    if (fixedGuns.Count > 0 && targeting is WCTargetingHelper)
                    {
                        WCTargetingHelper wcTargeting = (WCTargetingHelper)targeting;

                        wcTargeting.wAPI.GetBlockWeaponMap(fixedGuns.First(), weaponMap);
                        Echo($"WeaponCore initialized with {weaponMap.Count} weapon mappings");
                        wcInitSuccess = true;
                        doCcrp = true;
                    }
                    else if (fixedGuns.Count == 0)
                    {
                        Echo("No weapons found - disabling CCRP");
                        doCcrp = false;
                        wcInitSuccess = true; // Not an error, just no weapons
                    }
                }
                catch (Exception e)
                {
                    retryCount++;
                    Echo($"WeaponCore init attempt {retryCount} failed: {e.Message}");
                    if (retryCount >= 3)
                    {
                        Echo("WeaponCore initialization failed after 3 attempts - disabling CCRP");
                        doCcrp = false;
                        wcInitSuccess = true; // Stop retrying
                    }
                }
            }

            // Initialize other weapon systems
            try
            {
                // Get afterburners
                targeting.GetWeapons(allABs, abGroupName);
                Echo($"Found {allABs.Count} afterburners");
                RecalcABs();

                // Get flares
                targeting.GetWeapons(flares, flareGroupName);
                Echo($"Found {flares.Count} flares");

                // Reset ammo settings
                if (fixedGuns.Count > 0)
                {
                    string[] splitCustomData = new string[2];
                    IMyTerminalBlock b = fixedGuns[0];
                    if (b.CustomData != "")
                    {
                        splitCustomData = b.CustomData.Split('\n');
                        healAmmo = splitCustomData[0];
                        damageAmmo = splitCustomData[1];
                        Echo($"Restored ammo types - HEAL: {healAmmo}, DAMAGE: {damageAmmo}");
                    }
                }
            }
            catch (Exception e)
            {
                Echo("Error initializing secondary weapon systems: " + e.Message);
            }

            Echo("WeaponCore reinitialization complete");
        }

        private void ActiveDroneFrame2()
        {
            resultPos = aiTarget.IsEmpty() ? ctrlTargetPos : predictedTargetPos;

            // Adjust frame distribution based on performance
            bool isThrottled = averageRuntimeMS >= runtimeThreshold;
            int frameModulo = isThrottled ? 24 : 6; // Much slower when throttled

            switch (frame % frameModulo)
            {
                case 0:
                    if (doCcrp) CCRPHandler();
                    break;
                case 1:
                    // Only do obstruction checking if performance allows
                    if (!isThrottled || averageRuntimeMS < runtimeThreshold * 1.5)
                        targeting.GetObstructions(friendlies);
                    break;
                case 2:
                    AutoFlareHandler();
                    break;
                case 3:
                    // Update projectiles locked on (less critical when throttled)
                    if (targeting is WCTargetingHelper && (!isThrottled || averageRuntimeMS < runtimeThreshold * 1.2))
                    {
                        var wcTargeting = (WCTargetingHelper)targeting;
                        projectilesLockedOn = wcTargeting.wAPI.GetProjectilesLockedOn(gridId);
                    }

                    break;
            }

            // Extend intervals when throttled
            int performanceMultiplier = isThrottled ? 4 : (averageRuntimeMS > runtimeThreshold * 0.5 ? 2 : 1);

            if (frame % (60 * performanceMultiplier) == 0)
            {
                // Gets closest target - less frequent when throttled
                if (!healMode && (mode == 0 || autoTarget) &&
                    (!isThrottled || averageRuntimeMS < runtimeThreshold * 1.3))
                    targeting.SetTarget(targeting.GetClosestTarget());
                else if (healMode && autoTarget)
                    aiTarget = friendlies[0];

                // turn off guns if target is missing
                if (aiTarget.IsEmpty() && !healMode)
                    foreach (var weapon in fixedGuns)
                        Fire(weapon, false);

                // Thruster health check - less frequent when throttled
                bool needsRecalc = false;
                if (!isThrottled || averageRuntimeMS < runtimeThreshold * 1.5)
                {
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

                // Revengance status (always check this for safety)
                if (!(mode == 0 && autoTarget))
                {
                    if (DateTime.Now.Ticks - lastControllerPing > 100000000)
                    {
                        mode = 0;
                        autoTarget = true;
                        antenna.HudText = angryText[new Random().Next(angryText.Count())];
                    }
                }
            }

            // Cache clearing - much less frequent when throttled
            if (frame % (300 * performanceMultiplier * 2) == 0)
            {
                cachedPredictedPos = new Vector3D();
                cachedAmmoLeadPositions.Clear();
                weaponMap.Clear();
            }
        }

        private bool nearZone = false;

        void RunActiveDrone()
        {
            outText += "Locked onto " + aiTarget.Name + "\n";
            if (frame % 2 == 0) ActiveDroneFrame2();

            Vector3D aimPoint = new Vector3D();
            Vector3D aimDirection = Me.WorldMatrix.Forward; // Default to forward direction
            bool hasValidAimPoint = false;

            if (!aiTarget.IsEmpty())
            {
                if (targeting is WCTargetingHelper && fixedGuns.Count > 0)
                {
                    WCTargetingHelper wcTargeting = (WCTargetingHelper)targeting;

                    int weaponUpdateInterval = averageRuntimeMS > runtimeThreshold * 0.5 ? 120 : 60;
                    if (frame % weaponUpdateInterval == 0 && fixedGuns.First() != null)
                    {
                        weaponMap.Clear();
                        wcTargeting.wAPI.GetBlockWeaponMap(fixedGuns.First(), weaponMap);
                    }

                    if (weaponMap.Count > 0)
                    {
                        Vector3D? predictedPos = wcTargeting.wAPI.GetPredictedTargetPosition(fixedGuns.First(),
                            aiTarget.EntityId, weaponMap.First().Value);
                        if (predictedPos.HasValue && IsValidVector(predictedPos.Value))
                        {
                            cachedPredictedPos = predictedPos.Value;
                            hasValidAimPoint = true;
                        }
                    }

                    int ammoUpdateInterval = averageRuntimeMS > runtimeThreshold * 0.5 ? 60 : 30;
                    if (frame % ammoUpdateInterval == 0)
                    {
                        cachedPrimaryAmmo = wcTargeting.wAPI.GetActiveAmmo(fixedGuns.First(), weaponMap.First().Value);
                    }

                    if (hasValidAimPoint)
                    {
                        predictedTargetPos = cachedPredictedPos;
                        aimPoint = predictedTargetPos;
                    }
                }

                // If we have a target but no valid weapon prediction, use the target position
                if (!hasValidAimPoint && IsValidVector(aiTarget.Position))
                {
                    aimPoint = aiTarget.Position;
                    hasValidAimPoint = true;
                }

                // Calculate aim direction safely
                if (hasValidAimPoint)
                {
                    Vector3D aimVector = aimPoint - centerOfGrid;
                    if (IsValidVector(aimVector) && aimVector.LengthSquared() > 0.01) // Avoid near-zero vectors
                    {
                        aimDirection = Vector3D.Normalize(aimVector);
                        // Double-check the normalized vector is valid
                        if (!IsValidVector(aimDirection))
                        {
                            aimDirection = Me.WorldMatrix.Forward; // Fallback
                        }
                    }
                    else
                    {
                        aimDirection = Me.WorldMatrix.Forward; // Fallback for invalid/zero vectors
                    }
                }
            }
            else
            {
                // No target - use controller's forward direction if available
                if (IsValidMatrix(ctrlMatrix))
                {
                    aimDirection = ctrlMatrix.Forward;
                }
                else
                {
                    aimDirection = Me.WorldMatrix.Forward;
                }
            }

            Vector3D moveTo = new Vector3D();
            Vector3D stopPosition = CalcStopPosition(-Me.CubeGrid.LinearVelocity, centerOfGrid);

            // Validate stopPosition
            if (!IsValidVector(stopPosition))
            {
                stopPosition = centerOfGrid;
            }

            if (!aiTarget.IsEmpty() && averageRuntimeMS < runtimeThreshold && IsValidVector(aimPoint))
            {
                d.DrawLine(centerOfGrid, aimPoint, Color.Red, 0.1f);
            }

            if (averageRuntimeMS < runtimeThreshold && IsValidVector(stopPosition))
                d.DrawGPS("Stop Position", stopPosition);

            switch (mode)
            {
                case 0:
                    // Validate aimDirection before using it
                    if (IsValidVector(aimDirection))
                    {
                        gyros.FaceVectors(aimDirection, Me.WorldMatrix.Up);
                    }

                    if (!aiTarget.IsEmpty() && IsValidVector(aiTarget.Position))
                    {
                        Vector3D formationOffset =
                            Vector3D.Rotate(formationPresets[1][id] / formDistance * mainDistance, ctrlMatrix);
                        if (IsValidVector(formationOffset))
                        {
                            moveTo = aiTarget.Position + formationOffset;
                        }
                        else
                        {
                            moveTo = aiTarget.Position;
                        }
                    }
                    else
                    {
                        // No target - maintain formation relative to controller
                        Vector3D formationOffset = Vector3D.Rotate(formationPresets[formation][id], ctrlMatrix);
                        if (IsValidVector(formationOffset) && IsValidVector(controllerPos))
                        {
                            moveTo = controllerPos + formationOffset;
                        }
                        else
                        {
                            moveTo = centerOfGrid; // Stay put if calculations fail
                        }
                    }

                    if (averageRuntimeMS < runtimeThreshold && IsValidVector(moveTo))
                        d.DrawLine(centerOfGrid, moveTo, Color.Blue, 0.1f);
                    if (averageRuntimeMS < runtimeThreshold * 0.7)
                    {
                        closestCollision = CheckCollision(moveTo);
                        if (closestCollision != new Vector3D() && IsValidVector(closestCollision))
                        {
                            Vector3D avoidanceVector = moveTo.Cross(closestCollision);
                            if (IsValidVector(avoidanceVector))
                            {
                                moveTo += avoidanceVector;
                            }
                        }
                    }

                    break;

                case 1:
                    double dSq = Vector3D.DistanceSquared(controllerPos, Me.CubeGrid.GetPosition());
                    if (!healMode && damageAmmo != "" && targeting is WCTargetingHelper &&
                        frame % (averageRuntimeMS > runtimeThreshold * 0.5 ? 60 : 30) == 0)
                    {
                        WCTargetingHelper wCTargeting = (WCTargetingHelper)targeting;
                        foreach (var wep in fixedGuns)
                        {
                            if (wCTargeting.wAPI.GetActiveAmmo(wep, 0) == damageAmmo) break;
                            wep.SetValue<Int64>("WC_PickAmmo", 0);
                        }
                    }

                    Vector3D formationAimDirection;
                    if (dSq > formDistance * formDistance * 4 || healMode)
                    {
                        Vector3D toController = controllerPos - centerOfGrid;
                        if (IsValidVector(toController) && toController.LengthSquared() > 0.01)
                        {
                            formationAimDirection = Vector3D.Normalize(toController);
                        }
                        else
                        {
                            formationAimDirection = Me.WorldMatrix.Forward;
                        }
                    }
                    else if (!aiTarget.IsEmpty() && IsValidVector(aimDirection))
                    {
                        formationAimDirection = aimDirection;
                    }
                    else if (IsValidMatrix(ctrlMatrix))
                    {
                        formationAimDirection = ctrlMatrix.Forward; // No target - face same way as controller
                    }
                    else
                    {
                        formationAimDirection = Me.WorldMatrix.Forward;
                    }

                    // Validate formationAimDirection before using
                    if (!IsValidVector(formationAimDirection))
                    {
                        formationAimDirection = Me.WorldMatrix.Forward;
                    }

                    if (!aiTarget.IsEmpty() && !healMode && IsValidVector(aimDirection))
                    {
                        gyros.FaceVectors(aimDirection, Me.WorldMatrix.Up);
                    }
                    else
                    {
                        gyros.FaceVectors(formationAimDirection, Me.WorldMatrix.Up);
                    }

                    Vector3D formationPos = Vector3D.Rotate(formationPresets[formation][id], ctrlMatrix);
                    if (IsValidVector(formationPos) && IsValidVector(controllerPos))
                    {
                        moveTo = controllerPos + formationPos;
                    }
                    else
                    {
                        moveTo = centerOfGrid; // Stay put if calculations fail
                    }

                    if (averageRuntimeMS < runtimeThreshold)
                    {
                        if (IsValidVector(controllerPos))
                            d.DrawLine(centerOfGrid, controllerPos, Color.Green, 0.1f);
                        if (IsValidVector(moveTo))
                        {
                            d.DrawLine(centerOfGrid, moveTo, Color.Blue, 0.1f);
                            d.DrawGPS("Drone Position", moveTo);
                        }
                    }

                    if (averageRuntimeMS < runtimeThreshold * 0.7)
                    {
                        closestCollision = CheckCollision(moveTo);
                        if (closestCollision != new Vector3D() && IsValidVector(closestCollision))
                        {
                            Vector3D avoidanceVector = moveTo.Cross(closestCollision);
                            if (IsValidVector(avoidanceVector))
                            {
                                moveTo += avoidanceVector;
                            }
                        }
                    }

                    break;

                case 2:
                    if (IsValidVector(aimDirection))
                    {
                        gyros.FaceVectors(aimDirection, Me.WorldMatrix.Up);
                    }

                    Vector3D fortifyPos = formationPresets[formation][id];
                    if (IsValidVector(fortifyPos) && IsValidVector(controllerPos))
                    {
                        moveTo = controllerPos + fortifyPos;
                    }
                    else
                    {
                        moveTo = centerOfGrid;
                    }

                    if (averageRuntimeMS < runtimeThreshold * 0.7)
                    {
                        closestCollision = CheckCollision(moveTo);
                        if (closestCollision != new Vector3D() && IsValidVector(closestCollision))
                        {
                            Vector3D avoidanceVector = moveTo.Cross(closestCollision);
                            if (IsValidVector(avoidanceVector))
                            {
                                moveTo += avoidanceVector;
                            }
                        }
                    }

                    break;
            }

            // Final validation before thrust control
            if (IsValidVector(stopPosition) && IsValidVector(moveTo))
            {
                Vector3D thrustVector = stopPosition - moveTo;
                if (IsValidVector(thrustVector))
                {
                    ThrustControl(thrustVector, upThrust, downThrust, leftThrust, rightThrust, forwardThrust,
                        backThrust);
                }
            }
        }

        private bool IsValidVector(Vector3D vector)
        {
            return !double.IsNaN(vector.X) && !double.IsNaN(vector.Y) && !double.IsNaN(vector.Z) &&
                   !double.IsInfinity(vector.X) && !double.IsInfinity(vector.Y) && !double.IsInfinity(vector.Z);
        }

        private bool IsValidMatrix(MatrixD matrix)
        {
            return IsValidVector(matrix.Forward) && IsValidVector(matrix.Up) && IsValidVector(matrix.Right) &&
                   IsValidVector(matrix.Translation);
        }

        private void PrintDebugText()
        {
            outText += $"{Runtime.CurrentInstructionCount} instructions @ {Runtime.LastRunTimeMs:F2}ms\n";
            outText += $"Avg Runtime: {averageRuntimeMS:F2}ms\n";

            if (isController)
            {
                outText += $"Swarm Avg: {averageSwarmRuntimeMS:F2}ms | Drones: {droneEntities.Count}\n";
                outText +=
                    $"Controller: {(Runtime.LastRunTimeMs / 16.67 * 100):F1}% | Swarm: {(averageSwarmRuntimeMS / 16.67 * 100):F1}%\n";
            }

            // CHANGE THIS PART - send the average runtime instead of current runtime
            if (frame % 60 == 0) // Send every second instead of every 4 frames
            {
                if (!isController)
                    IGC.SendBroadcastMessage("per", averageRuntimeMS); // Send average instead of current
            }

            Echo(outText);
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
                        targeting.SetTarget(message.As<long>());
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
                    double droneAvgRuntime = performanceListener.AcceptMessage().As<double>();

                    if (isController)
                    {
                        // Simple: just store the runtimes and calculate average
                        currentFrameRuntimes.Add(droneAvgRuntime);
                    }

                    wasMessageRecieved = true;
                }
            }
            catch
            {
            }

            return wasMessageRecieved;
        }

        private void UpdateSwarmPerformanceAverage()
        {
            if (!isController || currentFrameRuntimes.Count == 0) return;

            // Simple average of all reported drone average runtimes
            double currentSwarmAverage = currentFrameRuntimes.Sum();

            // Update rolling average
            averageSwarmRuntimeMS = currentSwarmAverage;

            // Clear for next batch
            currentFrameRuntimes.Clear();
        }

        public void AutoIntegrity()
        {
            // AutoIntegrity System

            if (autoIntegrity && dAPI.GridHasShield(Me.CubeGrid) && !isController)
            {
                try
                {
                    if (distanceTarget < 40000 &&
                        !isIntegrity) // fuck you darkstar (x2). distanceTarget = distance squared.
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
                catch
                {
                }
            }
        }

        public void AutoFortify()
        {
            if (!autoFortify) return;

            // Check shield existence every 300 frames (5 seconds)
            if (frame % 300 == 0)
            {
                hasShield = dAPI.GridHasShield(Me.CubeGrid);
            }

            if (hasShield)
            {
                try
                {
                    if (speed < 12 && !isFortified)
                    {
                        shieldController.CustomData = "1";
                        toggleFort.Apply(shieldController);
                        isFortified = true;
                    }
                    else if (isFortified && speed > 12)
                    {
                        shieldController.CustomData = "0";
                        toggleFort.Apply(shieldController);
                        isFortified = false;
                    }
                }
                catch
                {
                }
            }
        }

        public void IGCSendHandler()
        {
            if (targeting.Target.EntityId != 0)
                SendGroupMsg<Vector3D>(targeting.Target.Position, false);

            if (frame % 20 == 0)
            {
                if (targeting.Target.EntityId != 0)
                    SendGroupMsg<long>(targeting.Target.EntityId, !multipleControllers || group == 0);

                if (frame % 240 == 0)
                {
                    SendGroupMsg<string>("c" + Me.CubeGrid.EntityId, true);
                }
            }

            if ((mode == 1) && frame % 2 == 0)
            {
                MatrixD m = cockpit.IsFunctional ? cockpit.WorldMatrix : Me.WorldMatrix;

                if (averageRuntimeMS < runtimeThreshold)
                    d.DrawLine(centerOfGrid, centerOfGrid + m.Up * 100, Color.Blue, 0.1f);


                if (rotate)
                    m *= Matrix.CreateFromAxisAngle(m.Forward, orbitsPerSecond % 360 / frame / 57.2957795f);

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

        public void CCRPHandler()
        {
            if (healMode)
            {
                outText += "Locked on controller\n";
                bool isLinedUp = Vector3D.Normalize(controllerPos - centerOfGrid).Dot(Me.WorldMatrix.Forward) >
                                 maxOffset;
                foreach (var weapon in fixedGuns)
                {
                    if (averageRuntimeMS < runtimeThreshold)
                        d.DrawLine(centerOfGrid, Me.WorldMatrix.Forward * targeting.GetMaxRange(weapon) + centerOfGrid,
                            Color.White, 0.5f);
                    if (isLinedUp && targeting.GetWeaponReady(weapon))
                    {
                        Fire(weapon, true);
                    }
                    else
                    {
                        Fire(weapon, false);
                    }
                }
            }
            else if (!aiTarget.IsEmpty() && fixedGuns.Count > 0 && targeting is WCTargetingHelper)
            {
                WCTargetingHelper wcTargeting = (WCTargetingHelper)targeting;

                // Update weapon maps AND lead positions together every 60 frames
                int updateInterval = averageRuntimeMS > runtimeThreshold * 0.5 ? 120 : 60;
                if (frame % updateInterval == weaponMapUpdateFrame)
                {
                    cachedWeaponMaps.Clear();
                    cachedAmmoLeadPositions.Clear();

                    foreach (var weapon in fixedGuns)
                    {
                        if (!weapon.IsFunctional) continue;
                        Dictionary<string, int> thisWeaponMap = new Dictionary<string, int>();
                        wcTargeting.wAPI.GetBlockWeaponMap(weapon, thisWeaponMap);
                        if (thisWeaponMap.Count > 0)
                        {
                            cachedWeaponMaps[weapon] = thisWeaponMap;

                            int weaponId = thisWeaponMap.First().Value;
                            string activeAmmo = wcTargeting.wAPI.GetActiveAmmo(weapon, weaponId);

                            if (activeAmmo != null && !cachedAmmoLeadPositions.ContainsKey(activeAmmo))
                            {
                                Vector3D? predictedPos =
                                    wcTargeting.wAPI.GetPredictedTargetPosition(weapon, aiTarget.EntityId, weaponId);
                                if (predictedPos.HasValue)
                                {
                                    cachedAmmoLeadPositions[activeAmmo] = predictedPos.Value;
                                }
                            }
                        }
                    }
                }

                // Check alignment and fire weapons using cached maps
                foreach (var weapon in fixedGuns)
                {
                    if (!weapon.IsFunctional || !cachedWeaponMaps.ContainsKey(weapon)) continue;

                    var thisWeaponMap = cachedWeaponMaps[weapon];
                    int weaponId = thisWeaponMap.First().Value;
                    bool shouldFire = wcTargeting.wAPI.IsWeaponReadyToFire(weapon);
                    bool isAligned = wcTargeting.wAPI.IsTargetAligned(weapon, aiTarget.EntityId, weaponId);
                    shouldFire = shouldFire && isAligned;

                    Fire(weapon, shouldFire);
                }

                // Debug output
                if (frame % 60 == 0 && cachedAmmoLeadPositions.Count > 0)
                {
                    outText += "Ammo Types: ";
                    foreach (var ammo in cachedAmmoLeadPositions.Keys)
                    {
                        outText += ammo + " ";
                    }

                    outText += "\n";
                }
            }
        }

        private void DrawLeadDebugs()
        {
            // Draw GPS markers EVERY FRAME using cached positions
            int ammoIndex = 0;
            foreach (var kvp in cachedAmmoLeadPositions)
            {
                string ammoType = kvp.Key;
                Vector3D leadPos = kvp.Value;

                Color gpsColor = ammoIndex == 0 ? Color.Red : (ammoIndex == 1 ? Color.Yellow : Color.Orange);

                d.DrawGPS($"Lead_{ammoType}", leadPos, gpsColor);
                d.DrawLine(centerOfGrid, leadPos, gpsColor, 0.5f);

                ammoIndex++;
            }
        }

        List<IMyTerminalBlock> usedFlares = new List<IMyTerminalBlock>();

        private void AutoFlareHandler()
        {
            // wait 5s between flares
            if (projectilesLockedOn.Item2 >= missilesToFlare && projectilesLockedOn.Item3 > minMissileAge)
            {
                foreach (var flare in flares)
                {
                    if (targeting.GetWeaponReady(flare))
                    {
                        targeting.FireWeapon(flare);
                        usedFlares.Add(flare);
                        break;
                    }
                }
            }

            foreach (var f in usedFlares)
                flares.Remove(f);

            usedFlares.Clear();
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

        public Vector3D CheckCollision(Vector3D stopPosition)
        {
            LineD r = new LineD(Me.CubeGrid.GetPosition(), stopPosition);

            foreach (var p in friendlies)
            {
                if (p.BoundingBox.Intersects(ref r))
                {
                    return p.Position;
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
                    healMode = false;
                    if (isController)
                        SendGroupMsg<string>("main", false);
                    antenna.HudText = id.ToString() + " | " + mode.ToString() + group.ToString();
                    return;
                case "wing":
                    mode = 1;
                    healMode = false;
                    if (isController)
                        SendGroupMsg<string>("wing", false);
                    antenna.HudText = id.ToString() + " | " + mode.ToString() + group.ToString();
                    return;
                case "fort":
                    mode = 2;
                    healMode = false;
                    if (isController)
                    {
                        SendGroupMsg<string>("fort", false);
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
                            SendGroupMsg<string>("start", true);
                            IGC.SendBroadcastMessage("-1", "start");
                        }

                        IGC.SendBroadcastMessage("pos", centerOfGrid);

                        IGC.SendBroadcastMessage("ori",
                            cockpit.IsFunctional ? cockpit.WorldMatrix : Me.WorldMatrix);
                    }

                    return;
                case "group":
                    if (group < 4) group++;
                    else group = 0;
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
                        SendGroupMsg<string>("stop", true);
                        IGC.SendBroadcastMessage("-1", "stop");
                    }

                    return;
                case "heal":
                    mode = 1;

                    if (isController)
                    {
                        SendGroupMsg<string>("heal", false);
                    }
                    else
                    {
                        if (healAmmo != "") healMode = true;
                        else return;
                        targeting.SetTarget(controlID);
                        foreach (var wep in fixedGuns)
                        {
                            wep.SetValue<long>("WC_PickAmmo", 1);
                        }
                    }

                    return;
                case "learnheal":
                    if (targeting is WCTargetingHelper)
                    {
                        WCTargetingHelper wcTargeting = (WCTargetingHelper)targeting;
                        foreach (var w in fixedGuns)
                        {
                            healAmmo = wcTargeting.wAPI.GetActiveAmmo(w, 0);
                            if (damageAmmo != "") w.CustomData = healAmmo + "\n" + damageAmmo;
                        }

                        Echo("Learned damage ammo " + healAmmo);
                    }
                    else
                        Echo("Weaponcore not active!");

                    break;
                case "learndamage":
                    if (targeting is WCTargetingHelper)
                    {
                        WCTargetingHelper wcTargeting = (WCTargetingHelper)targeting;
                        foreach (var w in fixedGuns)
                        {
                            damageAmmo = wcTargeting.wAPI.GetActiveAmmo(w, 0);
                            if (healAmmo != "") w.CustomData = healAmmo + "\n" + damageAmmo;
                        }

                        Echo("Learned damage ammo " + damageAmmo);
                    }
                    else
                        Echo("Weaponcore not active!");

                    break;
                case "ctrlgroup":
                    if (isController)
                    {
                        SendGroupMsg<string>("c" + Me.CubeGrid.EntityId, false);
                    }

                    break;

                case "recover":
                case "recycle":
                case "reset":
                    RecoverDrone();
                    return;
            }

            if (isController)
            {
                if (argument.Substring(0, 4) == "form")
                {
                    int.TryParse(argument.Substring(4), out formation);
                    SendGroupMsg<string>("f" + formation.ToString(), false);
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
                    {
                        id = int.Parse(argument.Substring(1, 2));
                        // Clear any stale target data when getting assigned an ID
                        aiTarget = new MyDetectedEntityInfo();
                        cachedPredictedPos = new Vector3D();
                        predictedTargetPos = new Vector3D();
                        targeting.SetTarget(new MyDetectedEntityInfo());
                    }

                    antenna.HudText = id.ToString() + " | " + mode.ToString() + group.ToString();
                    break;
                case 'e':
                    if (isController)
                    {
                        long tId = long.Parse(argument.Substring(1));
                        if (!droneEntities.Contains(tId)) droneEntities.Add(tId);
                        for (int i = 0; i < droneEntities.Count; i++)
                        {
                            SendGroupMsg<string>("i" + (i < 10 ? "0" + i.ToString() : i.ToString()) + droneEntities[i],
                                true);
                        }

                        antenna.HudText = id.ToString() + " | " + mode.ToString() + group.ToString();

                        // Send current target to all drones when a new one joins
                        if (targeting.Target.EntityId != 0)
                        {
                            SendGroupMsg<long>(targeting.Target.EntityId, true);
                            SendGroupMsg<Vector3D>(targeting.Target.Position, true);
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
                //default:
                //    long targetID;
                //    if (mode != 0 && !autoTarget && long.TryParse(argument, out targetID))
                //        wAPI.SetAiFocus(Me, targetID);
                //    break;
            }
        }

        Vector3D accel = new Vector3D();
        Vector3D accelR = new Vector3D();
        Vector3D timeToStop = new Vector3D();

        public Vector3D CalcStopPosition(Vector3D velocity, Vector3D gridCenter)
        {
            // Validate inputs
            if (!IsValidVector(velocity) || !IsValidVector(gridCenter))
            {
                return gridCenter; // Return current position if inputs are invalid
            }

            // Calculate acceleration for each (local) axis; 6 possible sides with thrust
            accel = new Vector3D(thrustAmt[4], thrustAmt[2], -thrustAmt[0]) / mass;
            accelR = new Vector3D(-thrustAmt[5], -thrustAmt[3], thrustAmt[1]) / mass;

            // Validate acceleration calculations
            if (!IsValidVector(accel) || !IsValidVector(accelR))
            {
                return gridCenter;
            }

            // Rotate (global -> local) velocity because Vector Math:tm:
            Vector3D rVelocity = Vector3D.Rotate(velocity, Me.WorldMatrix);

            if (!IsValidVector(rVelocity))
            {
                return gridCenter;
            }

            // Calculate time to stop for each (local) axis
            timeToStop = new Vector3D(
                Math.Abs(accel.X) > 0.001 && Math.Abs(accelR.X) > 0.001
                    ? (accel.X + rVelocity.X < rVelocity.X - accelR.X ? rVelocity.X / accel.X : rVelocity.X / accelR.X)
                    : 0,
                Math.Abs(accel.Y) > 0.001 && Math.Abs(accelR.Y) > 0.001
                    ? (accel.Y + rVelocity.Y < rVelocity.Y - accelR.Y ? rVelocity.Y / accel.Y : rVelocity.Y / accelR.Y)
                    : 0,
                Math.Abs(accel.Z) > 0.001 && Math.Abs(accelR.Z) > 0.001
                    ? (accel.Z + rVelocity.Z < rVelocity.Z - accelR.Z ? rVelocity.Z / accel.Z : rVelocity.Z / accelR.Z)
                    : 0
            );

            if (!IsValidVector(timeToStop))
            {
                return gridCenter;
            }

            // Distance from projected stop position to center
            Vector3D result = gridCenter - (velocity * timeToStop.Length()) / 2;

            if (!IsValidVector(result))
            {
                return gridCenter;
            }

            return result;
        }


        // In and out, 20 minute adventure.
        public void ThrustControl(Vector3D relPos, List<IMyThrust> up, List<IMyThrust> down, List<IMyThrust> left,
            List<IMyThrust> right, List<IMyThrust> forward, List<IMyThrust> back)
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
            tMove = new Vector3D(Vector3D.Dot(tMove, wm.Right), Vector3D.Dot(tMove, wm.Forward),
                Vector3D.Dot(tMove, wm.Up));

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
                if (tMove.X > 0.25)
                    foreach (var ab in leftAB)
                        targeting.FireWeapon(ab);
                if (-tMove.X > 0.25)
                    foreach (var ab in rightAB)
                        targeting.FireWeapon(ab);
                if (tMove.Y > 0.25)
                    foreach (var ab in forwardAB)
                        targeting.FireWeapon(ab);
                if (-tMove.Y > 0.25)
                    foreach (var ab in backAB)
                        targeting.FireWeapon(ab);
                if (tMove.Z > 0.25)
                    foreach (var ab in upAB)
                        targeting.FireWeapon(ab);
                if (-tMove.Z > 0.25)
                    foreach (var ab in downAB)
                        targeting.FireWeapon(ab);
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

        public void
            SendGroupMsg<T>(Object message,
                bool sendAll) // Shorthand so I don't have to type out like 50 chars every time I do an IGC call
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
    }
}