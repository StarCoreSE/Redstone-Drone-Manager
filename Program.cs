using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using VRage;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        #region mdk preserve

        // Originally made by Aristeas for StarCore, modified by Invalid.
        // https://github.com/StarCoreSE/Rocketman-Drone-Manager-Invalid

        /* CONFIG */

        /* GENERAL SETTINGS */

        // If this PB is on a drone, set to 'false'. If this PB is on a ship, set to 'true'.
        bool _isController;

        // Set this to true if multiple controllers are in use
        bool _multipleControllers = false;

        // If speed <12 m/s, fortify and shieldfit 0. Else unfortify. Default [true]
        bool _autoFortify = true;

        // If enemy <100m, enable structural integrity. Else unset integrity. Default [true]
        bool _autoIntegrity = true;

        // If true, find own target. If false, use controller target. Default [false]
        bool _autoTarget;

        // If true, rotate around controller grid. If false, remain fixed. Only applies to controller. Default [true]
        bool _rotate = true;

        // Speed of rotation around parent grid. Higher = slower. Default [6]
        float _orbitsPerSecond = 6;

        // How far (in meters) drones in formation should be from the controller. Default [250]
        static int _formDistance = 250;

        // How far (in meters) drones in 'main' mode should orbit from the target. Default [1000]
        int _mainDistance = 1000;

        // NEW:
        // WARNING: _debugDraw causes EXTREME performance impact on MS PB!
        // Only enable in offline singleplayer with Debug Draw API available.
        // Use at your own risk - will cause severe runtime spikes!
        bool _debugDraw = false;

        /* PERFORMANCE SETTINGS */

        // Runtime threshold in milliseconds - operations throttle above this (PER DRONE)
        double _runtimeThreshold = 0.05;

        // Exponential moving average significance for runtime tracking
        const double RuntimeSignificance = 0.005;


        /* DRONE SETTINGS */

        // Set this to the grid's mass (in KG) IF there is no controller (cockpit, remote control) on the grid.
        // put one of these on the fucking drone or i'll fucking show up in your room while you're asleep and steal shit -thecrystalwoods
        float _mass = 1300000;

        // Toggles if CCRP (autofire fixed guns) runs. Leave this on, I beg you. Default [TRUE]
        bool _doCcrp = true;

        // Maximum innacuracy of CCRP in degrees. A lower number = higher accuracy requirement. Default [2]
        double _maxOffset = 2;

        // Name of the terminal group containing the drone's fixed guns.
        string _gunGroupName = "Main";

        // Name of the terminal group containing the drone's afterburners. Make sure it's balanced!
        string _abGroupName = "Afterburners";

        // Name of the terminal group containing the drone's flares. Currently only supports single-use flares.
        string _flareGroupName = "Flares";

        // Minimum number of missiles targeting this grid before autoflare triggers.
        int _missilesToFlare = 4;

        // Minimum age in seconds of oldest missile before autoflare triggers.
        int _minMissileAge = 2;

        // Radius of the harm zone. this is in meters, not km.
        int _zoneRadius = 12000;


        // PID values

        #region PID values

        static double _kP = 32;
        static double _kI = 0;
        static double _kD = 32;
        static double _lowerBound = -1000;
        static double _upperBound = 1000;
        static double _timeStep = 1.0 / 60;

        #endregion

        string[] _angryText = new string[]
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

        #endregion

        // In Development Version //

        int _mode = 1;
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

        char _runIndicator = '|';

        PbApiWrapper _dApi;
        TargetingHelper _targeting;

        DebugAPI _d;

        double _averageRuntimeMs;

        bool _activated; // have I been told to move?

        Vector3D _centerOfGrid; // Me.position
        IMyCockpit _cockpit;

        long _frame;

        MyDetectedEntityInfo _aiTarget;

        GyroControl _gyros;

        IMyBroadcastListener _myBroadcastListener;
        IMyBroadcastListener _positionListener;
        IMyBroadcastListener _velocityListener;
        IMyBroadcastListener _orientListener;
        IMyBroadcastListener _performanceListener;


        int _group = 1; // Supports groups 1 through 4.

        List<long> _droneEntities = new List<long>();
        List<MyDetectedEntityInfo> _friendlies = new List<MyDetectedEntityInfo>();

        MyTuple<bool, int, int> _projectilesLockedOn = new MyTuple<bool, int, int>(false, 0, -1);

        public string OutText = ""; // Text buffer to avoid lag:tm:

        public long GridId;

        #region drone-specific

        long _controlId = 0;
        long _lastControllerPing;

        static readonly double Cos45 = Math.Sqrt(2) / 2;

        int _formation;

        readonly Vector3D[][] _formationPresets = new Vector3D[][]
        {
            new[] // X
            {
                // Max 32

                // Ring 1
                new Vector3D(_formDistance, 0, 0),
                new Vector3D(-_formDistance, 0, 0),
                new Vector3D(0, _formDistance, 0),
                new Vector3D(0, -_formDistance, 0),
                new Vector3D(Cos45 * _formDistance, Cos45 * _formDistance, 0),
                new Vector3D(Cos45 * _formDistance, -Cos45 * _formDistance, 0),
                new Vector3D(-Cos45 * _formDistance, Cos45 * _formDistance, 0),
                new Vector3D(-Cos45 * _formDistance, -Cos45 * _formDistance, 0),

                // Ring 2
                new Vector3D(1.5 * _formDistance, 0, 0),
                new Vector3D(-1.5 * _formDistance, 0, 0),
                new Vector3D(0, 1.5 * _formDistance, 0),
                new Vector3D(0, -1.5 * _formDistance, 0),
                new Vector3D(1.5 * Cos45 * _formDistance, 1.5 * Cos45 * _formDistance, 0),
                new Vector3D(1.5 * Cos45 * _formDistance, -1.5 * Cos45 * _formDistance, 0),
                new Vector3D(-1.5 * Cos45 * _formDistance, 1.5 * Cos45 * _formDistance, 0),
                new Vector3D(-1.5 * Cos45 * _formDistance, -1.5 * Cos45 * _formDistance, 0),

                // Ring 3
                new Vector3D(3.0 * _formDistance, 0, 0),
                new Vector3D(-3.0 * _formDistance, 0, 0),
                new Vector3D(0, 3.0 * _formDistance, 0),
                new Vector3D(0, -3.0 * _formDistance, 0),
                new Vector3D(3.0 * Cos45 * _formDistance, 3.0 * Cos45 * _formDistance, 0),
                new Vector3D(3.0 * Cos45 * _formDistance, -3.0 * Cos45 * _formDistance, 0),
                new Vector3D(-3.0 * Cos45 * _formDistance, 3.0 * Cos45 * _formDistance, 0),
                new Vector3D(-3.0 * Cos45 * _formDistance, -3.0 * Cos45 * _formDistance, 0),

                // Ring 4
                new Vector3D(4.5 * _formDistance, 0, 0),
                new Vector3D(-4.5 * _formDistance, 0, 0),
                new Vector3D(0, 4.5 * _formDistance, 0),
                new Vector3D(0, -4.5 * _formDistance, 0),
                new Vector3D(4.5 * Cos45 * _formDistance, 4.5 * Cos45 * _formDistance, 0),
                new Vector3D(4.5 * Cos45 * _formDistance, -4.5 * Cos45 * _formDistance, 0),
                new Vector3D(-4.5 * Cos45 * _formDistance, 4.5 * Cos45 * _formDistance, 0),
                new Vector3D(-4.5 * Cos45 * _formDistance, -4.5 * Cos45 * _formDistance, 0)
            },
            new[] // Sphere
            {
                // Max 14

                new Vector3D(_formDistance, 0, 0),
                new Vector3D(-_formDistance, 0, 0),
                new Vector3D(0, _formDistance, 0),
                new Vector3D(0, -_formDistance, 0),
                new Vector3D(0, 0, _formDistance),
                new Vector3D(0, 0, -_formDistance),

                new Vector3D(Cos45 * _formDistance, Cos45 * _formDistance, 0),
                new Vector3D(Cos45 * _formDistance, -Cos45 * _formDistance, 0),
                new Vector3D(-Cos45 * _formDistance, Cos45 * _formDistance, 0),
                new Vector3D(-Cos45 * _formDistance, -Cos45 * _formDistance, 0),
                new Vector3D(0, Cos45 * _formDistance, Cos45 * _formDistance),
                new Vector3D(0, Cos45 * _formDistance, -Cos45 * _formDistance),
                new Vector3D(0, -Cos45 * _formDistance, Cos45 * _formDistance),
                new Vector3D(0, -Cos45 * _formDistance, -Cos45 * _formDistance)
            },
            new[] // V
            {
                new Vector3D(_formDistance / 2.0, 0, -_formDistance / 2.0),
                new Vector3D(-_formDistance / 2.0, 0, -_formDistance / 2.0),
                new Vector3D(0, _formDistance / 2.0, -_formDistance / 2.0),
                new Vector3D(0, -_formDistance / 2.0, -_formDistance / 2.0),
                new Vector3D(_formDistance, 0, 0),
                new Vector3D(-_formDistance, 0, 0),
                new Vector3D(0, _formDistance, 0),
                new Vector3D(0, -_formDistance, 0),
            },
            new[] // duckling formation
            {
                new Vector3D(0, 0, _formDistance),
                new Vector3D(0, 0, 2 * _formDistance),
                new Vector3D(0, 0, 3 * _formDistance),
                new Vector3D(0, 0, 4 * _formDistance),
                new Vector3D(0, 0, 5 * _formDistance),
                new Vector3D(0, 0, 6 * _formDistance),
                new Vector3D(0, 0, 7 * _formDistance),
                new Vector3D(0, 0, 2 * _formDistance),
            },
        };

        string _damageAmmo = "";
        string _healAmmo = "";
        bool _healMode;

        MatrixD _ctrlMatrix;
        bool _isFortified;
        bool _isIntegrity;
        ITerminalAction _toggleFort;
        ITerminalAction _toggleIntegrity;
        Vector3D _predictedTargetPos; // target position + lead
        Vector3D _controllerPos; // center of controller ship

        Vector3D
            _anchorVelocity; // velocity of "anchored" target; controller if in WINGMAN mode, target if in MAIN mode.

        Vector3D _closestCollision;
        Vector3D _ctrlTargetPos;
        double _totalRuntime = 0;
        Vector3D _resultPos;
        double _distanceTarget = 0;
        bool _hasABs = false;

        double _speed;
        int _id = -1; // Per-drone ID. Used for formation flight. Controller is always -1

        double _totalSwarmRuntimeSum = 0; // Sum of all drone runtimes this frame
        int _swarmDroneCount = 0; // Number of drones that reported performance this frame
        double _currentSwarmRuntimeSum; // Rolling average of swarm performance
        List<double> _currentFrameRuntimes = new List<double>(); // Store runtimes for current frame

        double _lastSwarmCalculation = 0;
        int _performanceCalculationFrame = 0;

        #endregion

        #region Blocks

        List<IMyTerminalBlock> _allABs = new List<IMyTerminalBlock>();
        List<IMyTerminalBlock> _forwardAb = new List<IMyTerminalBlock>();
        List<IMyTerminalBlock> _backAb = new List<IMyTerminalBlock>();
        List<IMyTerminalBlock> _upAb = new List<IMyTerminalBlock>();
        List<IMyTerminalBlock> _downAb = new List<IMyTerminalBlock>();
        List<IMyTerminalBlock> _leftAb = new List<IMyTerminalBlock>();
        List<IMyTerminalBlock> _rightAb = new List<IMyTerminalBlock>();
        List<IMyTerminalBlock> _fixedGuns = new List<IMyTerminalBlock>();
        List<IMyTerminalBlock> _flares = new List<IMyTerminalBlock>();
        List<IMyThrust> _allThrust = new List<IMyThrust>();
        IMyRadioAntenna _antenna;

        IMyTerminalBlock _shieldController;
        IMyTerminalBlock _shieldModulator;

        /*
         * 0 forward
         * 1 back
         * 2 up
         * 3 down
         * 4 right
         * 5 left
         */
        double[] _thrustAmt = new double[6];
        List<IMyThrust> _forwardThrust = new List<IMyThrust>();
        List<IMyThrust> _backThrust = new List<IMyThrust>();
        List<IMyThrust> _upThrust = new List<IMyThrust>();
        List<IMyThrust> _downThrust = new List<IMyThrust>();
        List<IMyThrust> _leftThrust = new List<IMyThrust>();
        List<IMyThrust> _rightThrust = new List<IMyThrust>();

        List<IMyTextPanel> _outLcds = new List<IMyTextPanel>();

        Dictionary<string, int> _weaponMap = new Dictionary<string, int>();

        Dictionary<IMyTerminalBlock, Dictionary<string, int>> _cachedWeaponMaps =
            new Dictionary<IMyTerminalBlock, Dictionary<string, int>>();

        int _weaponMapUpdateFrame = 0;
        int _targetUpdateTimer;
        const int TargetUpdateInterval = 10; // Update every 10 ticks
        bool _hasShield;
        int _shieldCheckFrame = 0;

        Vector3D _cachedPredictedPos;
        int _predictedPosUpdateFrame = 0;

        Dictionary<string, Vector3D> _cachedAmmoLeadPositions = new Dictionary<string, Vector3D>();
        int _ammoLeadUpdateFrame = 0;
        string _cachedPrimaryAmmo = "";

        bool _pendingRecovery = false;

        bool _isDeploying = false;
        Vector3D _deployStartPos;
        Vector3D _deployDirection;
        double _deployDistance = 100;
        IMyShipConnector _connector;

        #endregion


        public Program()
        {
            TryInit();

            if (_isController)
                SendGroupMsg<string>("stop", true);
        }

        public void Save()
        {
            _frame = 0;
        }

        public void TryInit()
        {
            try
            {
                Init();
            }
            catch (Exception)
            {
                Echo(" !!! If you can see this, you're missing something important !!!");
                throw;
            }
        }

        void Init()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update100;

            // Init Targeting class
            if (TargetingHelper.WcPbApiExists(Me))
                _targeting = new WCTargetingHelper(this);
            else
                _targeting = new VanillaTargetingHelper(this);
            Echo("Initialized Targeting");

            // Init Defense Shields API
            _dApi = new PbApiWrapper(Me);
            Echo("Initialized dsAPI");

            // Init Debug Draw API
            _d = new DebugAPI(this);
            Echo("Initialized debugAPI");

            // Squares zoneRadius to avoid Vector3D.Distance() calls. Roots are quite unperformant.
            _zoneRadius *= _zoneRadius;

            // Force clear all WC-related caches that might be from a copied drone
            _weaponMap.Clear();
            _cachedWeaponMaps.Clear();
            _cachedAmmoLeadPositions.Clear();
            _cachedPredictedPos = new Vector3D();
            _predictedTargetPos = new Vector3D();
            _aiTarget = new MyDetectedEntityInfo();

            if (!_isController)
            {
                // Check for existing group config
                if (Me.CustomData == "" || Me.CustomData.Length > 2) Me.CustomData = "1";
                int.TryParse(Me.CustomData, out _group);
                if (_group == -1)
                {
                    _isController =
                        true; // IK this looks dumb. Goal is to make sure isController doesn't get accidentally set to false.
                    _group = 0;
                }
            }
            else Me.CustomData = "-1";

            Echo($"Checked customdata for group ({_group})");

            // Init Whip's GPS Gyro Control
            GridId = Me.CubeGrid.EntityId;
            _gyros = new GyroControl(this, Me, _kP, _kI, _kD, _lowerBound, _upperBound, _timeStep);
            Echo("Initialized Whip's Gyro Control");

            long droneGridId = Me.CubeGrid.EntityId;

            // Get cockpit for controller
            GridTerminalSystem.GetBlocksOfType<IMyCockpit>(null, b =>
            {
                if (b.CubeGrid.EntityId == droneGridId && b.CanControlShip)
                {
                    _cockpit = b;
                    return true;
                }
                return false;
            });
            Echo("Searched for cockpit on own grid " + (_cockpit == null ? "null" : _cockpit.CustomName));

            // Gets shield controllers. Also disables autofortify if no shield controllers are found.
            List<IMyTerminalBlock> shieldControllers = new List<IMyTerminalBlock>();
            List<IMyTerminalBlock> shieldModulators = new List<IMyTerminalBlock>();
            bool hasEnhancer = false;
            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(shieldControllers, b =>
            {
                if (b.CubeGrid.EntityId != droneGridId) return false;
                if (!hasEnhancer) hasEnhancer = b.DefinitionDisplayNameText.Contains("Enhancer");
                return b.CustomName.Contains("Shield Controller");
            });
            Echo($"Located {shieldControllers.Count} shield controllers");
            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(shieldModulators,
                b => { return b.CubeGrid.EntityId == droneGridId && b.CustomName.Contains("Shield Modulator"); });
            Echo($"Located {shieldModulators.Count} shield modulators");
            if (_autoFortify) _autoFortify = hasEnhancer;

            // Check if shield controller exists, set up shortcut actions for Fortify and Integrity.
            try
            {
                _shieldController = shieldControllers[0];
                _shieldController.GetActions(new List<ITerminalAction>(), b =>
                {
                    if (b.Id == "DS-C_ShieldFortify_Toggle") _toggleFort = b;
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
                _shieldModulator = shieldModulators[0];
                if (_autoIntegrity) _autoIntegrity = _shieldModulator != null;
                _shieldModulator?.GetActions(new List<ITerminalAction>(), b =>
                {
                    if (b.Id == "DS-M_ModulateReInforceProt_Toggle") _toggleIntegrity = b;
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
                if (b.CubeGrid.EntityId == droneGridId && b.CanControlShip)
                {
                    _mass = b.CalculateShipMass().TotalMass;
                    return true;
                }
                return false;
            });
            Echo("Set grid mass to " + _mass);

            // Set antenna ranges to 25k (save a tiny bit of power)
            GridTerminalSystem.GetBlocksOfType<IMyRadioAntenna>(null, b =>
            {
                if (b.CubeGrid.EntityId == droneGridId)
                {
                    b.Radius = 25000;
                    _antenna = b;
                    return true;
                }

                return false;
            });
            Echo("Set antenna radii to 25km");

            // Get LCDs with name containing 'Rocketman' and sets them up
            if (_isController)
                GridTerminalSystem.GetBlocksOfType(_outLcds, b =>
                    b.CubeGrid.EntityId == droneGridId && b.CustomName.ToLower().Contains("rocketman"));

            foreach (var l in _outLcds)
            {
                l.ContentType = ContentType.TEXT_AND_IMAGE;
                if (!l.CustomData.Contains("hudlcd")) l.CustomData = "hudlcd";
            }

            Echo($"Found {_outLcds.Count} LCDs");

            // Get fixed guns
            try
            {
                _targeting.GetWeapons(_fixedGuns, _gunGroupName);
                if (_fixedGuns.Count > 0 && _targeting is WCTargetingHelper)
                {
                    WCTargetingHelper wcTargeting = (WCTargetingHelper)_targeting;
                    wcTargeting.wAPI.GetBlockWeaponMap(_fixedGuns.First(), _weaponMap);
                }
            }
            catch
            {
                _doCcrp = false;
            }

            Echo($"Found {_fixedGuns.Count} fixed weapons");

            try
            {
                if (_fixedGuns.Count > 0)
                {
                    string[] splitCustomData;
                    IMyTerminalBlock b = _fixedGuns[0];
                    if (b.CustomData != "")
                    {
                        splitCustomData = b.CustomData.Split('\n');
                        _healAmmo = splitCustomData[0];
                        _damageAmmo = splitCustomData[1];

                        Echo($"Set ammo types to:\n    HEAL - {_healAmmo}\n    DAMAGE - {_damageAmmo}");
                    }
                }
            }
            catch
            {
                // ignored
            }

            // Get afterburners
            try
            {
                _targeting.GetWeapons(_allABs, _abGroupName);
            }
            catch
            {
                Echo("No afterburners detected!");
            }

            Echo($"Found {_allABs.Count} afterburners");

            // Sort afterburners
            RecalcABs();
            Echo($"Sorted {_allABs.Count} afterburners");

            List<IMyShipConnector> allConnectors = new List<IMyShipConnector>();
            GridTerminalSystem.GetBlocksOfType(allConnectors, c => c.CubeGrid.EntityId == droneGridId);
            _connector = allConnectors.FirstOrDefault();
            Echo($"Found {allConnectors.Count} connectors for deploy functionality");

            // Get all thrust
            GridTerminalSystem.GetBlocksOfType(_allThrust, t => t.CubeGrid.EntityId == GridId);
            Echo($"Found {_allThrust.Count} thrusters on own grid");

            // Sort thrust and calculate total thrust per direction
            RecalcThrust();
            Echo($"Sorted {_allThrust.Count} thrusters");

            // Set gyro and thrust override to 0
            _gyros.Reset();
            SetThrust(-1f, _allThrust, false);
            Echo("Reset thrust and gyro control");

            // Init IGC (Inter-Grid Communications) listener
            _myBroadcastListener = IGC.RegisterBroadcastListener(_isController ? "-1" : _group.ToString());
            Echo("Inited myBroadcastListener");

            _positionListener = IGC.RegisterBroadcastListener("pos" + (_multipleControllers ? _group.ToString() : ""));
            Echo("Inited positionListener");

            _velocityListener = IGC.RegisterBroadcastListener("vel" + (_multipleControllers ? _group.ToString() : ""));
            Echo("Inited velocityListener");

            _orientListener = IGC.RegisterBroadcastListener("ori" + (_multipleControllers ? _group.ToString() : ""));
            Echo("Inited orientListener");

            _performanceListener = IGC.RegisterBroadcastListener("per");
            Echo("Inited performanceListener");

            _antenna.HudText = "Awaiting " + (_isController ? "Drones!" : "Controller!");

            if (_isController)
            {
                SendGroupMsg<string>("r", true); // Reset drone IDs
                SendGroupMsg<string>("m" + _mainDistance, true);
                SendGroupMsg<string>("o" + _formDistance, true);
                Echo("Reset drone IDs and shared formation distances");
            }

            try
            {
                if (_autoFortify && _shieldController.CustomData == "1") _toggleFort.Apply(_shieldController);
            }
            catch
            {
                // ignored
            }

            // Convert maxOffset from human-readable format to dot product
            _maxOffset = Math.Cos(_maxOffset / 57.2957795);
            Echo("Set maxOffset to " + _maxOffset);

            // Cache flares for later use
            try
            {
                _targeting.GetWeapons(_flares, _flareGroupName);
            }
            catch
            {
                Echo("Missing flare group of name: " + _flareGroupName);
            }

            _minMissileAge *= 60;

            Echo("AutoFlare module initialized\n" + _flares.Count + " flares detected");

            GridId = Me.CubeGrid.EntityId;
            _frame = 0;

            Echo("Successfully initialized as a " + (_isController ? "controller." : "drone."));
            Echo($"Drone initialized on grid entity ID: {GridId}");
        }

        int _errorCounter;

        public void Main(string argument, UpdateType updateSource)
        {
            // Prevents running multiple times per tick
            if (updateSource == UpdateType.IGC)
                return;

            // Update performance average using EMA
            _averageRuntimeMs = RuntimeSignificance * Runtime.LastRunTimeMs +
                                (1 - RuntimeSignificance) * _averageRuntimeMs;

            if (!string.IsNullOrEmpty(argument) &&
                (argument == "recover" || argument == "recycle" || argument == "reset"))
            {
                if (_isController)
                {
                    // Broadcast the recover command to all drones
                    SendGroupMsg<string>("recover", true);
                    // Also broadcast to the general channel for any unassigned drones
                    IGC.SendBroadcastMessage("-1", "recover");
                }

                RecoverDrone();
                return;
            }
            else if ((!_isController || _mode == 1) && !_activated)
            {
                Runtime.UpdateFrequency = UpdateFrequency.Update100;
            }


            _d.RemoveAll();


            // Update last controller ping
            if (IgcHandler(updateSource))
                _lastControllerPing = DateTime.Now.Ticks;

            try
            {
                if (!string.IsNullOrEmpty(argument)) ParseCommands(argument.ToLower(), updateSource);
            }
            catch
            {
                // ignored
            }

            // Status info
            OutText +=
                $"M{_mode} : G{_group} : ID{_id} {(_activated ? "ACTIVE" : "INACTIVE")} {IndicateRun()}\n";
            OutText +=
                $"Runtime: {Runtime.LastRunTimeMs:F2}ms | Avg: {_averageRuntimeMs:F2}ms | Limit: {_runtimeThreshold}ms\n";
            OutText +=
                $"\nRocketman Drone Manager\n-------------------------\n{(_isController ? $"Controlling {_droneEntities.Count} drone(s)" : "Drone Mode")}\n";

            // If ID unset and is not controller, ping controller for ID.
            if (_id == -1 && !_isController)
            {
                _activated = false;
                IGC.SendBroadcastMessage("-1", "e" + Me.CubeGrid.EntityId);

                // Draw debug elements even when inactive

                DrawDebugElements();


                return;
            }

            if (_activated) // Generic "I'm on!" stuff
            {
                if (_isDeploying)
                {
                    UpdateDeploy();

                    // Draw debug elements during deploy

                    DrawDebugElements();
                    PrintDebugText();


                    if (_averageRuntimeMs < _runtimeThreshold)
                    {
                        foreach (var l in _outLcds)
                            l.WriteText(OutText);
                    }

                    OutText = "";
                    _frame++;
                    return; // Don't run normal drone logic while deploying
                }

                try
                {
                    // ALWAYS do these critical operations regardless of performance
                    _centerOfGrid = Me.CubeGrid.GetPosition();

                    // Smart throttling - spread work across frames when performance is poor
                    bool isThrottled = _averageRuntimeMs >= _runtimeThreshold;
                    int throttleFrames = isThrottled ? 6 : 1; // Spread work across 6 frames when throttled

                    // Frame-distributed operations (only when throttled)
                    if (!isThrottled || (_frame % throttleFrames) == 0)
                    {
                        // Only update targeting every 10 ticks (or less frequently if throttled)
                        int targetingInterval = isThrottled ? TargetUpdateInterval * 3 : TargetUpdateInterval;
                        if (_targetUpdateTimer++ >= targetingInterval)
                        {
                            _targeting.Update();
                            _targetUpdateTimer = 0;
                        }
                    }

                    if (!isThrottled || (_frame % throttleFrames) == 1)
                    {
                        if (!_healMode)
                        {
                            var currentTarget = _targeting.Target;
                            // Only update aiTarget if we have a valid target from the targeting system
                            if (!currentTarget.IsEmpty())
                            {
                                _aiTarget = currentTarget;
                            }
                            // If targeting system has no target but we previously had one, clear it
                            else if (currentTarget.IsEmpty() && !_aiTarget.IsEmpty())
                            {
                                _aiTarget = new MyDetectedEntityInfo();
                                _cachedPredictedPos = new Vector3D();
                                _predictedTargetPos = new Vector3D();
                            }
                        }
                    }

                    if (!isThrottled || (_frame % throttleFrames) == 2)
                    {
                        OutText += "Velocity " + _speed + "\n";

                        // Update speed and auto-systems less frequently when throttled
                        int systemUpdateInterval = isThrottled ? 120 : 60;
                        if (_frame % systemUpdateInterval == 0)
                        {
                            _speed = Me.CubeGrid.LinearVelocity.Length();
                            AutoFortify();
                            AutoIntegrity();
                        }
                    }

                    if (!isThrottled || (_frame % throttleFrames) == 3)
                    {
                        if (_isController)
                        {
                            IgcSendHandler();
                            UpdateSwarmRuntimeSum();
                        }
                    }

                    if (!_isController) //throttling this will make it impossible to aim fug
                    {
                        //if (!isThrottled || (frame % throttleFrames) == 4)
                        //{
                        RunActiveDrone();
                        //}
                    }

                    // Add throttling indicator to output
                    if (isThrottled)
                    {
                        OutText += $"THROTTLED ({throttleFrames}x slower)\n";
                    }

                    _errorCounter = 0;
                }
                // Scary error handling
                catch (Exception e)
                {
                    if (_errorCounter > 10)
                    {
                        if (_antenna != null)
                            _antenna.HudText += " [CRASHED]";
                        throw;
                    }

                    Me.GetSurface(0).WriteText(e.ToString());
                    _errorCounter++;
                }
            }

            DrawDebugElements();
            PrintDebugText();

            // Check for WeaponCore issues in active drones
            if (_activated && !_doCcrp && _fixedGuns.Count > 0 &&
                _frame % (_averageRuntimeMs > _runtimeThreshold ? 120 : 60) == 0)
            {
                Echo("WeaponCore not active but weapons present - attempting recovery");
                ReinitializeWeaponCore();
            }

            // Auto-detect if we might be a copy with conflicting ID
            if (_activated && _id != -1 && !_isController &&
                _frame % (_averageRuntimeMs > _runtimeThreshold ? 600 : 300) == 0)
            {
                // Check if our stored grid ID doesn't match our actual grid ID
                if (GridId != Me.CubeGrid.EntityId)
                {
                    Echo("Grid ID mismatch detected - likely a copied drone!");
                    Echo($"Stored ID: {GridId}, Actual ID: {Me.CubeGrid.EntityId}");
                    RecoverDrone();
                    return;
                }
            }

            if (_activated && !_doCcrp && _fixedGuns.Count > 0)
            {
                Echo("WeaponCore not active but weapons present - attempting recovery");
                ReinitializeWeaponCore();
            }

            if (_averageRuntimeMs < _runtimeThreshold)
            {
                foreach (var l in _outLcds)
                    l.WriteText(OutText);
            }

            OutText = "";

            _frame++;
        }

        void RecoverDrone()
        {
            Echo("nitiating drone recovery...");

            // Reset core drone state
            _id = -1;
            _activated = false;
            _isDeploying = false;
            _lastControllerPing = 0;
            _aiTarget = new MyDetectedEntityInfo();
            _cachedPredictedPos = new Vector3D();
            _predictedTargetPos = new Vector3D();
            _droneEntities.Clear();
            _friendlies.Clear();

            // Reset control systems
            if (_gyros != null) _gyros.Reset();
            SetThrust(-1f, _allThrust, false);

            // Reinitialize WeaponCore
            ReinitializeWeaponCore();

            // Reset grid tracking
            GridId = Me.CubeGrid.EntityId;

            // Reinitialize communication listeners
            _myBroadcastListener = IGC.RegisterBroadcastListener(_isController ? "-1" : _group.ToString());
            _positionListener = IGC.RegisterBroadcastListener("pos" + (_multipleControllers ? _group.ToString() : ""));
            _velocityListener = IGC.RegisterBroadcastListener("vel" + (_multipleControllers ? _group.ToString() : ""));
            _orientListener = IGC.RegisterBroadcastListener("ori" + (_multipleControllers ? _group.ToString() : ""));
            _performanceListener = IGC.RegisterBroadcastListener("per");

            // Update status
            if (_antenna != null) _antenna.HudText = "Recovery Complete - Ready for Combat!";

            // Announce presence to controller (important for copied drones)
            IGC.SendBroadcastMessage("-1", "e" + Me.CubeGrid.EntityId);

            // Reset frame counter
            _frame = 0;

            Echo("Drone recovery completed - Grid ID: " + GridId + "");
        }

        void ReinitializeWeaponCore()
        {
            Echo("Reinitializing WeaponCore systems...");

            // Clear existing weapon data
            _fixedGuns.Clear();
            _allABs.Clear();
            _flares.Clear();
            _weaponMap.Clear();
            _cachedWeaponMaps.Clear();
            _cachedAmmoLeadPositions.Clear();

            // Reset WeaponCore state
            _doCcrp = true;

            // Reinitialize targeting helper
            try
            {
                if (TargetingHelper.WcPbApiExists(Me))
                    _targeting = new WCTargetingHelper(this);
                else
                    _targeting = new VanillaTargetingHelper(this);
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
                    _targeting.GetWeapons(_fixedGuns, _gunGroupName);
                    Echo($"Found {_fixedGuns.Count} weapons");

                    // Initialize WeaponCore API if we have weapons
                    if (_fixedGuns.Count > 0 && _targeting is WCTargetingHelper)
                    {
                        WCTargetingHelper wcTargeting = (WCTargetingHelper)_targeting;

                        wcTargeting.wAPI.GetBlockWeaponMap(_fixedGuns.First(), _weaponMap);
                        Echo($"WeaponCore initialized with {_weaponMap.Count} weapon mappings");
                        wcInitSuccess = true;
                        _doCcrp = true;
                    }
                    else if (_fixedGuns.Count == 0)
                    {
                        Echo("No weapons found - disabling CCRP");
                        _doCcrp = false;
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
                        _doCcrp = false;
                        wcInitSuccess = true; // Stop retrying
                    }
                }
            }

            // Initialize other weapon systems
            try
            {
                // Get afterburners
                _targeting.GetWeapons(_allABs, _abGroupName);
                Echo($"Found {_allABs.Count} afterburners");
                RecalcABs();

                // Get flares
                _targeting.GetWeapons(_flares, _flareGroupName);
                Echo($"Found {_flares.Count} flares");

                // Reset ammo settings
                if (_fixedGuns.Count > 0)
                {
                    string[] splitCustomData;
                    IMyTerminalBlock b = _fixedGuns[0];
                    if (b.CustomData != "")
                    {
                        splitCustomData = b.CustomData.Split('\n');
                        _healAmmo = splitCustomData[0];
                        _damageAmmo = splitCustomData[1];
                        Echo($"Restored ammo types - HEAL: {_healAmmo}, DAMAGE: {_damageAmmo}");
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
            _resultPos = _aiTarget.IsEmpty() ? _ctrlTargetPos : _predictedTargetPos;

            // Adjust frame distribution based on performance
            bool isThrottled = _averageRuntimeMs >= _runtimeThreshold;
            int frameModulo = isThrottled ? 24 : 6;

            switch (_frame % frameModulo)
            {
                case 0:
                    if (_doCcrp) CcrpHandler();
                    break;
                case 1:
                    _targeting.GetObstructions(_friendlies);
                    break;
                case 2:
                    AutoFlareHandler();
                    break;
                case 3:
                    if (_targeting is WCTargetingHelper)
                    {
                        var wcTargeting = (WCTargetingHelper)_targeting;
                        _projectilesLockedOn = wcTargeting.wAPI.GetProjectilesLockedOn(GridId);
                    }

                    break;
            }

            // Extend intervals when throttled but don't double-check performance
            int performanceMultiplier = isThrottled ? 4 : 1;

            if (_frame % (60 * performanceMultiplier) == 0)
            {
                if (!_healMode && (_mode == 0 || _autoTarget))
                    _targeting.SetTarget(_targeting.GetClosestTarget());
                else if (_healMode && _autoTarget)
                    _aiTarget = _friendlies[0];

                // turn off guns if target is missing
                if (_aiTarget.IsEmpty() && !_healMode)
                    foreach (var weapon in _fixedGuns)
                        Fire(weapon, false);

                bool needsRecalc = false;
                foreach (var t in _allThrust)
                {
                    if (t.WorldAABB == new BoundingBoxD())
                    {
                        needsRecalc = true;
                        break;
                    }
                }

                if (needsRecalc)
                {
                    GridTerminalSystem.GetBlocksOfType(_allThrust, t => t.CubeGrid.EntityId == Me.CubeGrid.EntityId);
                    RecalcThrust();
                    OutText += $"Recalculated thrust with {_allThrust.Count} thrusters";
                }

                // Revengance status (always check this for safety)
                if (!(_mode == 0 && _autoTarget))
                {
                    if (DateTime.Now.Ticks - _lastControllerPing > 100000000)
                    {
                        _mode = 0;
                        _autoTarget = true;
                        _antenna.HudText = _angryText[new Random().Next(_angryText.Count())];
                    }
                }
            }

            // Cache clearing - much less frequent when throttled
            if (_frame % (300 * performanceMultiplier * 2) == 0)
            {
                _cachedPredictedPos = new Vector3D();
                _cachedAmmoLeadPositions.Clear();
                _weaponMap.Clear();
            }
        }

        void RunActiveDrone()
        {
            bool hasValidTarget = !_aiTarget.IsEmpty() && IsValidVector(_aiTarget.Position) && _aiTarget.EntityId != 0;
            OutText += hasValidTarget ? $"Locked onto {_aiTarget.Name}\n" : "No valid target\n";

            // Debug info for controller
            if (_cockpit != null && _cockpit.IsFunctional)
            {
                OutText += "Controller cockpit: FUNCTIONAL\n";
                OutText += $"Controller pos: {_cockpit.GetPosition()}\n";
            }
            else
            {
                OutText += "Controller cockpit: NOT FUNCTIONAL\n";
            }

            if (IsValidMatrix(_ctrlMatrix))
            {
                OutText += "Controller matrix: VALID\n";
            }
            else
            {
                OutText += "Controller matrix: INVALID\n";
            }

            if (_frame % 2 == 0)
                ActiveDroneFrame2();

            bool isThrottled = _averageRuntimeMs >= _runtimeThreshold;
            Vector3D aimPoint = new Vector3D();
            Vector3D aimDirection = Me.WorldMatrix.Forward;
            bool hasValidAimPoint = false;

            if (hasValidTarget)
            {
                if (_targeting is WCTargetingHelper && _fixedGuns.Count > 0)
                {
                    WCTargetingHelper wcTargeting = (WCTargetingHelper)_targeting;
                    int weaponUpdateInterval = isThrottled ? 120 : 60;
                    if (_frame % weaponUpdateInterval == 0 && _fixedGuns.First() != null)
                    {
                        _weaponMap.Clear();
                        wcTargeting.wAPI.GetBlockWeaponMap(_fixedGuns.First(), _weaponMap);
                    }

                    if (_weaponMap.Count > 0)
                    {
                        Vector3D? predictedPos = wcTargeting.wAPI.GetPredictedTargetPosition(_fixedGuns.First(),
                            _aiTarget.EntityId, _weaponMap.First().Value);
                        if (predictedPos.HasValue && IsValidVector(predictedPos.Value))
                        {
                            _cachedPredictedPos = predictedPos.Value;
                            hasValidAimPoint = true;
                        }
                    }

                    int ammoUpdateInterval = isThrottled ? 60 : 30;
                    if (_frame % ammoUpdateInterval == 0 && _weaponMap.Count > 0)
                    {
                        _cachedPrimaryAmmo =
                            wcTargeting.wAPI.GetActiveAmmo(_fixedGuns.First(), _weaponMap.First().Value);
                    }

                    if (hasValidAimPoint)
                    {
                        _predictedTargetPos = _cachedPredictedPos;
                        aimPoint = _predictedTargetPos;
                    }
                }

                if (!hasValidAimPoint)
                {
                    aimPoint = _aiTarget.Position;
                    hasValidAimPoint = true;
                }

                if (hasValidAimPoint)
                {
                    Vector3D aimVector = aimPoint - _centerOfGrid;
                    if (IsValidVector(aimVector) && aimVector.LengthSquared() > 0.01)
                    {
                        aimDirection = Vector3D.Normalize(aimVector);
                        if (!IsValidVector(aimDirection))
                        {
                            aimDirection = Me.WorldMatrix.Forward;
                        }
                    }
                    else
                    {
                        aimDirection = Me.WorldMatrix.Forward;
                    }
                }
            }

            Vector3D moveTo = new Vector3D();
            Vector3D stopPosition = CalcStopPosition(-Me.CubeGrid.LinearVelocity, _centerOfGrid);
            if (!IsValidVector(stopPosition))
            {
                stopPosition = _centerOfGrid;
            }

            // Get controller direction - prefer the actual cockpit over the matrix
            Vector3D controllerForward = Me.WorldMatrix.Forward;
            Vector3D controllerUp = Me.WorldMatrix.Up;

            if (_cockpit != null && _cockpit.IsFunctional)
            {
                controllerForward = _cockpit.WorldMatrix.Forward;
                controllerUp = _cockpit.WorldMatrix.Up;
                OutText += "Using cockpit orientation\n";
            }
            else if (IsValidMatrix(_ctrlMatrix))
            {
                controllerForward = _ctrlMatrix.Forward;
                controllerUp = _ctrlMatrix.Up;
                OutText += "Using matrix orientation\n";
            }
            else
            {
                OutText += "No controller orientation available\n";
            }

            switch (_mode)
            {
                case 0:
                    if (hasValidTarget)
                    {
                        if (IsValidVector(aimDirection))
                        {
                            _gyros.FaceVectors(aimDirection, Me.WorldMatrix.Up);
                            OutText += "Mode 0: Aiming at target\n";
                        }
                    }
                    else
                    {
                        // No target - face controller direction
                        if (IsValidVector(controllerForward))
                        {
                            _gyros.FaceVectors(controllerForward, controllerUp);
                            OutText += "Mode 0: Facing controller direction\n";
                        }
                        else
                        {
                            OutText += "Mode 0: No valid controller direction\n";
                        }
                    }

                    if (hasValidTarget)
                    {
                        Vector3D formationOffset =
                            Vector3D.Rotate(_formationPresets[1][_id] / _formDistance * _mainDistance, _ctrlMatrix);
                        if (IsValidVector(formationOffset))
                        {
                            moveTo = _aiTarget.Position + formationOffset;
                        }
                        else
                        {
                            moveTo = _aiTarget.Position;
                        }
                    }
                    else
                    {
                        Vector3D formationOffset = Vector3D.Zero;
                        if (_cockpit != null && _cockpit.IsFunctional)
                        {
                            formationOffset = Vector3D.Rotate(_formationPresets[_formation][_id], _cockpit.WorldMatrix);
                        }
                        else if (IsValidMatrix(_ctrlMatrix))
                        {
                            formationOffset = Vector3D.Rotate(_formationPresets[_formation][_id], _ctrlMatrix);
                        }

                        if (IsValidVector(formationOffset) && IsValidVector(_controllerPos))
                        {
                            moveTo = _controllerPos + formationOffset;
                        }
                        else
                        {
                            moveTo = _centerOfGrid;
                        }
                    }

                    if (!isThrottled)
                    {
                        _closestCollision = CheckCollision(moveTo);
                        if (_closestCollision != new Vector3D() && IsValidVector(_closestCollision))
                        {
                            Vector3D avoidanceVector = moveTo.Cross(_closestCollision);
                            if (IsValidVector(avoidanceVector))
                            {
                                moveTo += avoidanceVector;
                            }
                        }
                    }

                    break;

                case 1:
                    double dSq = Vector3D.DistanceSquared(_controllerPos, Me.CubeGrid.GetPosition());
                    if (!_healMode && _damageAmmo != "" && _targeting is WCTargetingHelper &&
                        _frame % (isThrottled ? 60 : 30) == 0)
                    {
                        WCTargetingHelper wCTargeting = (WCTargetingHelper)_targeting;
                        foreach (var wep in _fixedGuns)
                        {
                            if (wCTargeting.wAPI.GetActiveAmmo(wep, 0) == _damageAmmo)
                                break;
                            wep.SetValue<Int64>("WC_PickAmmo", 0);
                        }
                    }

                    Vector3D formationAimDirection;
                    Vector3D formationUpDirection = Me.WorldMatrix.Up;

                    if (dSq > _formDistance * _formDistance * 4 || _healMode)
                    {
                        Vector3D toController = _controllerPos - _centerOfGrid;
                        if (IsValidVector(toController) && toController.LengthSquared() > 0.01)
                        {
                            formationAimDirection = Vector3D.Normalize(toController);
                            OutText += "Mode 1: Facing controller (distant)\n";
                        }
                        else
                        {
                            formationAimDirection = controllerForward;
                            formationUpDirection = controllerUp;
                            OutText += "Mode 1: Using controller forward (distant)\n";
                        }
                    }
                    else if (hasValidTarget && IsValidVector(aimDirection))
                    {
                        formationAimDirection = aimDirection;
                        OutText += "Mode 1: Aiming at target\n";
                    }
                    else
                    {
                        formationAimDirection = controllerForward;
                        formationUpDirection = controllerUp;
                        OutText += "Mode 1: Facing controller direction\n";
                    }

                    if (!IsValidVector(formationAimDirection))
                    {
                        formationAimDirection = Me.WorldMatrix.Forward;
                        formationUpDirection = Me.WorldMatrix.Up;
                    }

                    _gyros.FaceVectors(formationAimDirection, formationUpDirection);

                    Vector3D formationPos = Vector3D.Zero;
                    if (_cockpit != null && _cockpit.IsFunctional)
                    {
                        formationPos = Vector3D.Rotate(_formationPresets[_formation][_id], _cockpit.WorldMatrix);
                    }
                    else if (IsValidMatrix(_ctrlMatrix))
                    {
                        formationPos = Vector3D.Rotate(_formationPresets[_formation][_id], _ctrlMatrix);
                    }

                    if (IsValidVector(formationPos) && IsValidVector(_controllerPos))
                    {
                        moveTo = _controllerPos + formationPos;
                    }
                    else
                    {
                        moveTo = _centerOfGrid;
                    }

                    if (!isThrottled)
                    {
                        _closestCollision = CheckCollision(moveTo);
                        if (_closestCollision != new Vector3D() && IsValidVector(_closestCollision))
                        {
                            Vector3D avoidanceVector = moveTo.Cross(_closestCollision);
                            if (IsValidVector(avoidanceVector))
                            {
                                moveTo += avoidanceVector;
                            }
                        }
                    }

                    break;

                case 2:
                    if (hasValidTarget)
                    {
                        if (IsValidVector(aimDirection))
                        {
                            _gyros.FaceVectors(aimDirection, Me.WorldMatrix.Up);
                            OutText += "Mode 2: Aiming at target\n";
                        }
                    }
                    else
                    {
                        // No target - face controller direction
                        if (IsValidVector(controllerForward))
                        {
                            _gyros.FaceVectors(controllerForward, controllerUp);
                            OutText += "Mode 2: Facing controller direction\n";
                        }
                        else
                        {
                            OutText += "Mode 2: No valid controller direction\n";
                        }
                    }

                    Vector3D fortifyPos = Vector3D.Zero;
                    if (_cockpit != null && _cockpit.IsFunctional)
                    {
                        fortifyPos = Vector3D.Rotate(_formationPresets[_formation][_id], _cockpit.WorldMatrix);
                    }
                    else if (IsValidMatrix(_ctrlMatrix))
                    {
                        fortifyPos = Vector3D.Rotate(_formationPresets[_formation][_id], _ctrlMatrix);
                    }
                    else
                    {
                        fortifyPos = _formationPresets[_formation][_id];
                    }

                    if (IsValidVector(fortifyPos) && IsValidVector(_controllerPos))
                    {
                        moveTo = _controllerPos + fortifyPos;
                    }
                    else
                    {
                        moveTo = _centerOfGrid;
                    }

                    if (!isThrottled)
                    {
                        _closestCollision = CheckCollision(moveTo);
                        if (_closestCollision != new Vector3D() && IsValidVector(_closestCollision))
                        {
                            Vector3D avoidanceVector = moveTo.Cross(_closestCollision);
                            if (IsValidVector(avoidanceVector))
                            {
                                moveTo += avoidanceVector;
                            }
                        }
                    }

                    break;
            }

            if (IsValidVector(stopPosition) && IsValidVector(moveTo))
            {
                Vector3D thrustVector = stopPosition - moveTo;
                if (IsValidVector(thrustVector))
                {
                    ThrustControl(thrustVector, _upThrust, _downThrust, _leftThrust, _rightThrust, _forwardThrust,
                        _backThrust);
                }
            }
        }

        void DrawDebugElements()
        {
            if (!_debugDraw) return;

            // Draw drone forward direction (always visible)
            Vector3D droneForwardEnd = _centerOfGrid + Me.WorldMatrix.Forward * 500;
            _d.DrawLine(_centerOfGrid, droneForwardEnd, Color.Cyan, 0.2f);
            _d.DrawGPS("Drone_Forward", droneForwardEnd, Color.Cyan);

            // Draw controller forward direction - prefer cockpit over matrix
            Vector3D controllerPos = _centerOfGrid;
            Vector3D controllerForward = Me.WorldMatrix.Forward;

            if (_cockpit != null && _cockpit.IsFunctional)
            {
                controllerPos = _cockpit.GetPosition();
                controllerForward = _cockpit.WorldMatrix.Forward;
                Vector3D controllerForwardEnd = controllerPos + controllerForward * 500;
                _d.DrawLine(controllerPos, controllerForwardEnd, Color.Magenta, 0.2f);
                _d.DrawGPS("Cockpit_Forward", controllerForwardEnd, Color.Magenta);
                _d.DrawGPS("Controller_Cockpit", controllerPos, Color.Green);
            }

            // Draw line between controller and drone
            if (IsValidVector(_controllerPos))
            {
                _d.DrawLine(_centerOfGrid, _controllerPos, Color.White, 0.1f);
            }

            if (!_aiTarget.IsEmpty() && IsValidVector(_aiTarget.Position))
            {
                _d.DrawGPS("AI Target", _aiTarget.Position, Color.Red);
                _d.DrawLine(_centerOfGrid, _aiTarget.Position, Color.Red, 0.1f);
            }

            if (IsValidVector(_predictedTargetPos) && _predictedTargetPos != Vector3D.Zero)
            {
                _d.DrawGPS("Predicted Target", _predictedTargetPos, Color.Orange);
                _d.DrawLine(_centerOfGrid, _predictedTargetPos, Color.Orange, 0.15f);
            }

            if (IsValidVector(_controllerPos) && _controllerPos != Vector3D.Zero && _lastControllerPing > 0)
            {
                _d.DrawGPS("Controller", _controllerPos, Color.Green);
                _d.DrawLine(_centerOfGrid, _controllerPos, Color.Green, 0.1f);
            }

            if (IsValidVector(_resultPos) && _resultPos != Vector3D.Zero)
            {
                _d.DrawGPS("Move Target", _resultPos, Color.Blue);
                _d.DrawLine(_centerOfGrid, _resultPos, Color.Blue, 0.1f);
            }

            // Formation debug for mode 1
            if (_mode == 1 && _id >= 0 && _id < _formationPresets[_formation].Length)
            {
                Vector3D formationOffset = Vector3D.Zero;
                if (_cockpit != null && _cockpit.IsFunctional)
                {
                    formationOffset = Vector3D.Rotate(_formationPresets[_formation][_id], _cockpit.WorldMatrix);
                }
                else if (IsValidMatrix(_ctrlMatrix))
                {
                    formationOffset = Vector3D.Rotate(_formationPresets[_formation][_id], _ctrlMatrix);
                }

                if (IsValidVector(formationOffset) && IsValidVector(_controllerPos))
                {
                    Vector3D formationPos = _controllerPos + formationOffset;
                    _d.DrawGPS($"Formation_{_id}", formationPos, Color.Yellow);
                    _d.DrawLine(_centerOfGrid, formationPos, Color.Yellow, 0.08f);
                }
            }

            if (_fixedGuns.Count > 0 && !_aiTarget.IsEmpty())
            {
                foreach (var weapon in _fixedGuns)
                {
                    if (weapon != null && weapon.IsFunctional)
                    {
                        try
                        {
                            double range = _targeting.GetMaxRange(weapon);
                            Vector3D weaponEnd = _centerOfGrid + Me.WorldMatrix.Forward * range;
                            _d.DrawLine(_centerOfGrid, weaponEnd, Color.White, 0.05f);
                        }
                        catch
                        {
                        }
                    }
                }
            }

            if (_closestCollision != Vector3D.Zero && IsValidVector(_closestCollision))
            {
                _d.DrawLine(_centerOfGrid, _closestCollision, Color.Orange, 0.2f);
                _d.DrawGPS("Collision Risk", _closestCollision, Color.Orange);
            }

            Vector3D stopPosition = CalcStopPosition(-Me.CubeGrid.LinearVelocity, _centerOfGrid);
            if (IsValidVector(stopPosition) && stopPosition != _centerOfGrid)
            {
                _d.DrawGPS("Stop Position", stopPosition, Color.Cyan);
                _d.DrawLine(_centerOfGrid, stopPosition, Color.Cyan, 0.05f);
            }

            if (IsValidVector(Me.CubeGrid.LinearVelocity) && Me.CubeGrid.LinearVelocity.LengthSquared() > 1)
            {
                Vector3D velocityEnd = _centerOfGrid + Me.CubeGrid.LinearVelocity * 10;
                _d.DrawLine(_centerOfGrid, velocityEnd, Color.Purple, 0.08f);
            }

            if (_isDeploying)
            {
                if (IsValidVector(_deployStartPos))
                {
                    _d.DrawGPS("Deploy Start", _deployStartPos, Color.White);
                }

                Vector3D deployTarget = _deployStartPos + (_deployDirection * _deployDistance);
                if (IsValidVector(deployTarget))
                {
                    _d.DrawGPS("Deploy Target", deployTarget, Color.Lime);
                    _d.DrawLine(_deployStartPos, deployTarget, Color.Lime, 0.15f);
                }
            }

            DrawLeadDebugs();
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
            int startInstructions = Runtime.CurrentInstructionCount;
            OutText += $"Debug overhead: {Runtime.CurrentInstructionCount - startInstructions} instructions\n";
            OutText += $"{Runtime.CurrentInstructionCount} total instructions @ {Runtime.LastRunTimeMs:F2}ms\n";

            if (_isController)
            {
                OutText +=
                    $"Swarm Sum: {_currentSwarmRuntimeSum:F2}ms | Drones: {_droneEntities.Count}\n"; // Changed from "Swarm Avg"
                OutText +=
                    $"Controller: {(Runtime.LastRunTimeMs / 16.67 * 100):F1}% | Swarm Total: {(_currentSwarmRuntimeSum / 16.67 * 100):F1}%\n";
            }

            if (_frame % 60 == 0)
            {
                if (!_isController)
                    IGC.SendBroadcastMessage("per", _averageRuntimeMs);
            }

            Echo(OutText);
        }

        public bool IgcHandler(UpdateType updateSource)
        {
            bool isThrottled = _averageRuntimeMs >= _runtimeThreshold;
            if (isThrottled && _frame % 3 != 0) return false;

            bool wasMessageRecieved = false;
            // If IGC message recieved
            try
            {
                while (_myBroadcastListener.HasPendingMessage)
                {
                    MyIGCMessage message = _myBroadcastListener.AcceptMessage();

                    if (message.Data is long && !_isController)
                    {
                        _targeting.SetTarget(message.As<long>());
                    }
                    else if (message.Data is Vector3D)
                        _ctrlTargetPos = message.As<Vector3D>();
                    else
                        ParseCommands(message.Data.ToString(), updateSource);

                    wasMessageRecieved = true;
                }

                while (_positionListener.HasPendingMessage)
                {
                    _controllerPos = _positionListener.AcceptMessage().As<Vector3D>();
                    if (!_activated && _id != -1)
                    {
                        _activated = true;
                        _centerOfGrid = Me.CubeGrid.GetPosition();
                        Runtime.UpdateFrequency = UpdateFrequency.Update1;
                    }

                    wasMessageRecieved = true;
                }

                if (_mode == 1)
                {
                    while (_velocityListener.HasPendingMessage)
                    {
                        _anchorVelocity = _velocityListener.AcceptMessage().As<Vector3D>();
                        wasMessageRecieved = true;
                    }
                }

                while (_orientListener.HasPendingMessage)
                {
                    _ctrlMatrix = _orientListener.AcceptMessage().As<MatrixD>();
                    wasMessageRecieved = true;
                }

                while (_performanceListener.HasPendingMessage)
                {
                    double droneAvgRuntime = _performanceListener.AcceptMessage().As<double>();

                    if (_isController)
                    {
                        // Simple: just store the runtimes and calculate average
                        _currentFrameRuntimes.Add(droneAvgRuntime);
                    }

                    wasMessageRecieved = true;
                }
            }
            catch
            {
                // ignored
            }

            return wasMessageRecieved;
        }

        private void UpdateSwarmRuntimeSum() // was UpdateSwarmPerformanceAverage
        {
            if (!_isController || _currentFrameRuntimes.Count == 0)
                return;

            // Calculate sum instead of average
            double currentSwarmSum = _currentFrameRuntimes.Sum();
            _currentSwarmRuntimeSum = currentSwarmSum; // This is now the SUM
            _currentFrameRuntimes.Clear();
        }

        public void AutoIntegrity()
        {
            // AutoIntegrity System

            if (_autoIntegrity && _dApi.GridHasShield(Me.CubeGrid) && !_isController)
            {
                try
                {
                    if (_distanceTarget < 40000 &&
                        !_isIntegrity) // fuck you darkstar (x2). distanceTarget = distance squared.
                    {
                        _toggleIntegrity.Apply(_shieldModulator);
                        _isIntegrity = true; // "dead reckoning" system for autointegrity. Breaks if messed with. )))
                    }
                    else if (_isIntegrity && _distanceTarget > 40000)
                    {
                        _toggleIntegrity.Apply(_shieldModulator);
                        _isIntegrity = false;
                    }
                }
                catch
                {
                    // ignored
                }
            }
        }

        public void AutoFortify()
        {
            if (!_autoFortify) return;

            // Check shield existence every 300 frames (5 seconds)
            if (_frame % 300 == 0)
            {
                _hasShield = _dApi.GridHasShield(Me.CubeGrid);
            }

            if (_hasShield)
            {
                try
                {
                    if (_speed < 12 && !_isFortified)
                    {
                        _shieldController.CustomData = "1";
                        _toggleFort.Apply(_shieldController);
                        _isFortified = true;
                    }
                    else if (_isFortified && _speed > 12)
                    {
                        _shieldController.CustomData = "0";
                        _toggleFort.Apply(_shieldController);
                        _isFortified = false;
                    }
                }
                catch
                {
                    // ignored
                }
            }
        }

        public void IgcSendHandler()
        {
            if (_targeting.Target.EntityId != 0)
                SendGroupMsg<Vector3D>(_targeting.Target.Position, false);

            if (_frame % 20 == 0)
            {
                if (_targeting.Target.EntityId != 0)
                    SendGroupMsg<long>(_targeting.Target.EntityId, !_multipleControllers || _group == 0);

                if (_frame % 240 == 0)
                {
                    SendGroupMsg<string>("c" + Me.CubeGrid.EntityId, true);
                }
            }

            if ((_mode == 1) && _frame % 2 == 0)
            {
                MatrixD m = _cockpit.IsFunctional ? _cockpit.WorldMatrix : Me.WorldMatrix;

                if (_rotate)
                    m *= Matrix.CreateFromAxisAngle(m.Forward, _orbitsPerSecond % 360 / _frame / 57.2957795f);

                if (_multipleControllers)
                {
                    if (_group != 0)
                    {
                        IGC.SendBroadcastMessage("pos" + _group, _centerOfGrid);
                        IGC.SendBroadcastMessage("vel" + _group, -Me.CubeGrid.LinearVelocity);
                        IGC.SendBroadcastMessage("ori" + _group, m);
                    }
                    else
                    {
                        for (int i = 1; i < 5; i++)
                        {
                            IGC.SendBroadcastMessage("pos" + i, _centerOfGrid);
                            IGC.SendBroadcastMessage("vel" + i, -Me.CubeGrid.LinearVelocity);
                            IGC.SendBroadcastMessage("ori" + i, m);
                        }
                    }
                }
                else
                {
                    IGC.SendBroadcastMessage("pos", _centerOfGrid);
                    IGC.SendBroadcastMessage("vel", -Me.CubeGrid.LinearVelocity);
                    IGC.SendBroadcastMessage("ori", m);
                }
            }
        }

        public void CcrpHandler()
        {
            bool isThrottled = _averageRuntimeMs >= _runtimeThreshold;

            if (_healMode)
            {
                OutText += "Locked on controller\n";
                bool isLinedUp = Vector3D.Normalize(_controllerPos - _centerOfGrid).Dot(Me.WorldMatrix.Forward) >
                                 _maxOffset;
                foreach (var weapon in _fixedGuns)
                {
                    if (isLinedUp && _targeting.GetWeaponReady(weapon))
                    {
                        Fire(weapon, true);
                    }
                    else
                    {
                        Fire(weapon, false);
                    }
                }
            }
            else if (!_aiTarget.IsEmpty() && _fixedGuns.Count > 0 && _targeting is WCTargetingHelper)
            {
                WCTargetingHelper wcTargeting = (WCTargetingHelper)_targeting;

                int updateInterval = isThrottled ? 120 : 60;
                if (_frame % updateInterval == _weaponMapUpdateFrame)
                {
                    _cachedWeaponMaps.Clear();
                    _cachedAmmoLeadPositions.Clear();

                    foreach (var weapon in _fixedGuns)
                    {
                        if (!weapon.IsFunctional) continue;
                        Dictionary<string, int> thisWeaponMap = new Dictionary<string, int>();
                        wcTargeting.wAPI.GetBlockWeaponMap(weapon, thisWeaponMap);
                        if (thisWeaponMap.Count > 0)
                        {
                            _cachedWeaponMaps[weapon] = thisWeaponMap;

                            int weaponId = thisWeaponMap.First().Value;
                            string activeAmmo = wcTargeting.wAPI.GetActiveAmmo(weapon, weaponId);

                            if (activeAmmo != null && !_cachedAmmoLeadPositions.ContainsKey(activeAmmo))
                            {
                                Vector3D? predictedPos =
                                    wcTargeting.wAPI.GetPredictedTargetPosition(weapon, _aiTarget.EntityId, weaponId);
                                if (predictedPos.HasValue)
                                {
                                    _cachedAmmoLeadPositions[activeAmmo] = predictedPos.Value;
                                }
                            }
                        }
                    }
                }

                // Check alignment and fire weapons using cached maps
                foreach (var weapon in _fixedGuns)
                {
                    if (!weapon.IsFunctional || !_cachedWeaponMaps.ContainsKey(weapon)) continue;

                    var thisWeaponMap = _cachedWeaponMaps[weapon];
                    int weaponId = thisWeaponMap.First().Value;
                    bool shouldFire = wcTargeting.wAPI.IsWeaponReadyToFire(weapon);
                    bool isAligned = wcTargeting.wAPI.IsTargetAligned(weapon, _aiTarget.EntityId, weaponId);
                    shouldFire = shouldFire && isAligned;

                    Fire(weapon, shouldFire);
                }

                // Debug output
                if (_frame % 60 == 0 && _cachedAmmoLeadPositions.Count > 0)
                {
                    OutText += "Ammo Types: ";
                    foreach (var ammo in _cachedAmmoLeadPositions.Keys)
                    {
                        OutText += ammo + " ";
                    }

                    OutText += "\n";
                }
            }
        }

        private void DrawLeadDebugs()
        {
            // Draw GPS markers EVERY FRAME using cached positions
            int ammoIndex = 0;
            foreach (var kvp in _cachedAmmoLeadPositions)
            {
                string ammoType = kvp.Key;
                Vector3D leadPos = kvp.Value;

                Color gpsColor = ammoIndex == 0 ? Color.Red : (ammoIndex == 1 ? Color.Yellow : Color.Orange);

                _d.DrawGPS($"Lead_{ammoType}", leadPos, gpsColor);
                _d.DrawLine(_centerOfGrid, leadPos, gpsColor, 0.5f);

                ammoIndex++;
            }
        }

        List<IMyTerminalBlock> _usedFlares = new List<IMyTerminalBlock>();

        private void AutoFlareHandler()
        {
            // wait 5s between flares
            if (_projectilesLockedOn.Item2 >= _missilesToFlare && _projectilesLockedOn.Item3 > _minMissileAge)
            {
                foreach (var flare in _flares)
                {
                    if (_targeting.GetWeaponReady(flare))
                    {
                        _targeting.FireWeapon(flare);
                        _usedFlares.Add(flare);
                        break;
                    }
                }
            }

            foreach (var f in _usedFlares)
                _flares.Remove(f);

            _usedFlares.Clear();
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

            foreach (var p in _friendlies)
            {
                var boundingBox = p.BoundingBox;
                if (boundingBox.Intersects(ref r))
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
                    _mode = 0;
                    _healMode = false;
                    if (_isController)
                        SendGroupMsg<string>("main", false);
                    _antenna.HudText = _id.ToString() + " | " + _mode.ToString() + _group.ToString();
                    return;
                case "wing":
                    _mode = 1;
                    _healMode = false;
                    if (_isController)
                        SendGroupMsg<string>("wing", false);
                    _antenna.HudText = _id.ToString() + " | " + _mode.ToString() + _group.ToString();
                    return;
                case "fort":
                    _mode = 2;
                    _healMode = false;
                    if (_isController)
                    {
                        SendGroupMsg<string>("fort", false);
                        IGC.SendBroadcastMessage("pos", _centerOfGrid);
                        MatrixD m = _cockpit.IsFunctional ? _cockpit.WorldMatrix : Me.WorldMatrix;

                        IGC.SendBroadcastMessage("ori", m);
                    }

                    _antenna.HudText = _id.ToString() + " | " + _mode.ToString() + _group.ToString();
                    return;
                case "start":
                    _activated = true;
                    _centerOfGrid = Me.CubeGrid.GetPosition();
                    _lastControllerPing = DateTime.Now.Ticks;
                    Runtime.UpdateFrequency = UpdateFrequency.Update1;
                    if (_isController)
                    {
                        if (updateSource != UpdateType.IGC)
                        {
                            SendGroupMsg<string>("start", true);
                            IGC.SendBroadcastMessage("-1", "start");
                        }

                        IGC.SendBroadcastMessage("pos", _centerOfGrid);

                        IGC.SendBroadcastMessage("ori",
                            _cockpit.IsFunctional ? _cockpit.WorldMatrix : Me.WorldMatrix);
                    }

                    return;
                case "group":
                    if (_group < 4) _group++;
                    else _group = 0;
                    _antenna.HudText = _id.ToString() + " | " + _mode.ToString() + _group.ToString();
                    return;
                case "stop":
                    _activated = false;
                    SetThrust(-1f, _allThrust, false);
                    _gyros.Reset();
                    if (_doCcrp)
                    {
                        foreach (var weapon in _fixedGuns)
                            Fire(weapon, false);
                    }

                    if (_isController && updateSource != UpdateType.IGC)
                    {
                        SendGroupMsg<string>("stop", true);
                        IGC.SendBroadcastMessage("-1", "stop");
                    }

                    return;
                case "heal":
                    _mode = 1;

                    if (_isController)
                    {
                        SendGroupMsg<string>("heal", false);
                    }
                    else
                    {
                        if (_healAmmo != "") _healMode = true;
                        else return;
                        _targeting.SetTarget(_controlId);
                        foreach (var wep in _fixedGuns)
                        {
                            wep.SetValue<long>("WC_PickAmmo", 1);
                        }
                    }

                    return;
                case "learnheal":
                    if (_targeting is WCTargetingHelper)
                    {
                        WCTargetingHelper wcTargeting = (WCTargetingHelper)_targeting;
                        foreach (var w in _fixedGuns)
                        {
                            _healAmmo = wcTargeting.wAPI.GetActiveAmmo(w, 0);
                            if (_damageAmmo != "") w.CustomData = _healAmmo + "\n" + _damageAmmo;
                        }

                        Echo("Learned damage ammo " + _healAmmo);
                    }
                    else
                        Echo("Weaponcore not active!");

                    break;
                case "learndamage":
                    if (_targeting is WCTargetingHelper)
                    {
                        WCTargetingHelper wcTargeting = (WCTargetingHelper)_targeting;
                        foreach (var w in _fixedGuns)
                        {
                            _damageAmmo = wcTargeting.wAPI.GetActiveAmmo(w, 0);
                            if (_healAmmo != "") w.CustomData = _healAmmo + "\n" + _damageAmmo;
                        }

                        Echo("Learned damage ammo " + _damageAmmo);
                    }
                    else
                        Echo("Weaponcore not active!");

                    break;
                case "ctrlgroup":
                    if (_isController)
                    {
                        SendGroupMsg<string>("c" + Me.CubeGrid.EntityId, false);
                    }

                    break;

                case "deploy":
                    if (!_isController)
                    {
                        StartDeploy();
                    }
                    else if (_isController && updateSource != UpdateType.IGC)
                    {
                        SendGroupMsg<string>("deploy", true);
                        IGC.SendBroadcastMessage("-1", "deploy");
                    }

                    return;

                case "recover":
                case "recycle":
                case "reset":
                    RecoverDrone();
                    return;
            }

            if (_isController)
            {
                if (argument.Substring(0, 4) == "form")
                {
                    int.TryParse(argument.Substring(4), out _formation);
                    SendGroupMsg<string>("f" + _formation.ToString(), false);
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
                    if (!_isController && long.Parse(argument.Substring(3)) == Me.CubeGrid.EntityId)
                    {
                        _id = int.Parse(argument.Substring(1, 2));
                        // Clear any stale target data when getting assigned an ID
                        _aiTarget = new MyDetectedEntityInfo();
                        _cachedPredictedPos = new Vector3D();
                        _predictedTargetPos = new Vector3D();
                        _targeting.SetTarget(new MyDetectedEntityInfo());
                    }

                    _antenna.HudText = _id.ToString() + " | " + _mode.ToString() + _group.ToString();
                    break;
                case 'e':
                    if (_isController)
                    {
                        long tId = long.Parse(argument.Substring(1));
                        if (!_droneEntities.Contains(tId)) _droneEntities.Add(tId);
                        for (int i = 0; i < _droneEntities.Count; i++)
                        {
                            SendGroupMsg<string>("i" + (i < 10 ? "0" + i.ToString() : i.ToString()) + _droneEntities[i],
                                true);
                        }

                        _antenna.HudText = _id.ToString() + " | " + _mode.ToString() + _group.ToString();

                        // Send current target to all drones when a new one joins
                        if (_targeting.Target.EntityId != 0)
                        {
                            SendGroupMsg<long>(_targeting.Target.EntityId, true);
                            SendGroupMsg<Vector3D>(_targeting.Target.Position, true);
                        }
                    }

                    break;
                case 'r':
                    if (!_isController)
                    {
                        _id = -1;
                    }

                    break;
                case 'm':
                    if (!_isController)
                    {
                        int.TryParse(argument.Substring(1), out _mainDistance);
                    }

                    break;
                case 'o':
                    if (!_isController)
                    {
                        int.TryParse(argument.Substring(1), out _formDistance);
                    }

                    break;
                case 'f':
                    int.TryParse(argument.Substring(1), out _formation);
                    if (_formation + 1 > _formationPresets.Length) _formation = _formationPresets.Length - 1;
                    break;
                //default:
                //    long targetID;
                //    if (mode != 0 && !autoTarget && long.TryParse(argument, out targetID))
                //        wAPI.SetAiFocus(Me, targetID);
                //    break;
            }
        }

        Vector3D _accel;
        Vector3D _accelR;
        Vector3D _timeToStop;

        public Vector3D CalcStopPosition(Vector3D velocity, Vector3D gridCenter)
        {
            // Validate inputs
            if (!IsValidVector(velocity) || !IsValidVector(gridCenter))
            {
                return gridCenter; // Return current position if inputs are invalid
            }

            // Calculate acceleration for each (local) axis; 6 possible sides with thrust
            _accel = new Vector3D(_thrustAmt[4], _thrustAmt[2], -_thrustAmt[0]) / _mass;
            _accelR = new Vector3D(-_thrustAmt[5], -_thrustAmt[3], _thrustAmt[1]) / _mass;

            // Validate acceleration calculations
            if (!IsValidVector(_accel) || !IsValidVector(_accelR))
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
            _timeToStop = new Vector3D(
                Math.Abs(_accel.X) > 0.001 && Math.Abs(_accelR.X) > 0.001
                    ? (_accel.X + rVelocity.X < rVelocity.X - _accelR.X
                        ? rVelocity.X / _accel.X
                        : rVelocity.X / _accelR.X)
                    : 0,
                Math.Abs(_accel.Y) > 0.001 && Math.Abs(_accelR.Y) > 0.001
                    ? (_accel.Y + rVelocity.Y < rVelocity.Y - _accelR.Y
                        ? rVelocity.Y / _accel.Y
                        : rVelocity.Y / _accelR.Y)
                    : 0,
                Math.Abs(_accel.Z) > 0.001 && Math.Abs(_accelR.Z) > 0.001
                    ? (_accel.Z + rVelocity.Z < rVelocity.Z - _accelR.Z
                        ? rVelocity.Z / _accel.Z
                        : rVelocity.Z / _accelR.Z)
                    : 0
            );

            if (!IsValidVector(_timeToStop))
            {
                return gridCenter;
            }

            // Distance from projected stop position to center
            Vector3D result = gridCenter - (velocity * _timeToStop.Length()) / 2;

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
            if (_hasABs && relPos.LengthSquared() > 10000)
            {
                if (tMove.X > 0.25)
                    foreach (var ab in _leftAb)
                        _targeting.FireWeapon(ab);
                if (-tMove.X > 0.25)
                    foreach (var ab in _rightAb)
                        _targeting.FireWeapon(ab);
                if (tMove.Y > 0.25)
                    foreach (var ab in _forwardAb)
                        _targeting.FireWeapon(ab);
                if (-tMove.Y > 0.25)
                    foreach (var ab in _backAb)
                        _targeting.FireWeapon(ab);
                if (tMove.Z > 0.25)
                    foreach (var ab in _upAb)
                        _targeting.FireWeapon(ab);
                if (-tMove.Z > 0.25)
                    foreach (var ab in _downAb)
                        _targeting.FireWeapon(ab);
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
            _forwardThrust.Clear();
            _backThrust.Clear();
            _upThrust.Clear();
            _downThrust.Clear();
            _leftThrust.Clear();
            _rightThrust.Clear();
            Array.Clear(_thrustAmt, 0, _thrustAmt.Length);

            foreach (var thrust in _allThrust)
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
                    _forwardThrust.Add(thrust);
                    _thrustAmt[0] += thrust.MaxEffectiveThrust;
                    continue;
                }

                if (thrust.Orientation.Forward == Base6Directions.GetOppositeDirection(Me.Orientation.Up))
                {
                    _upThrust.Add(thrust);
                    _thrustAmt[2] += thrust.MaxEffectiveThrust;
                    continue;
                }

                if (thrust.Orientation.Forward == Base6Directions.GetOppositeDirection(Me.Orientation.Left))
                {
                    _leftThrust.Add(thrust);
                    _thrustAmt[5] += thrust.MaxEffectiveThrust;
                    continue;
                }

                if (thrust.Orientation.Forward == Me.Orientation.Forward)
                {
                    _backThrust.Add(thrust);
                    _thrustAmt[1] += thrust.MaxEffectiveThrust;
                    continue;
                }

                if (thrust.Orientation.Forward == Me.Orientation.Up)
                {
                    _downThrust.Add(thrust);
                    _thrustAmt[3] += thrust.MaxEffectiveThrust;
                    continue;
                }

                if (thrust.Orientation.Forward == Me.Orientation.Left)
                {
                    _rightThrust.Add(thrust);
                    _thrustAmt[4] += thrust.MaxEffectiveThrust;
                }
            }
        }

        public void RecalcABs()
        {
            // Sort afterburners
            foreach (var ab in _allABs)
            {
                if (ab.Orientation.Forward == Base6Directions.GetOppositeDirection(Me.Orientation.Forward))
                {
                    _forwardAb.Add(ab);
                }

                if (ab.Orientation.Forward == Base6Directions.GetOppositeDirection(Me.Orientation.Up))
                {
                    _upAb.Add(ab);
                }

                if (ab.Orientation.Forward == Base6Directions.GetOppositeDirection(Me.Orientation.Left))
                {
                    _leftAb.Add(ab);
                }

                if (ab.Orientation.Forward == Me.Orientation.Forward)
                {
                    _backAb.Add(ab);
                }

                if (ab.Orientation.Forward == Me.Orientation.Up)
                {
                    _downAb.Add(ab);
                }

                if (ab.Orientation.Forward == Me.Orientation.Left)
                {
                    _rightAb.Add(ab);
                }
            }
        }


        public double RoundPlaces(double d, int place)
        {
            return ((int)(d * Math.Pow(10, place))) / Math.Pow(10, place);
        }

        public double TwrCalc(int dir)
        {
            return _thrustAmt[dir] / _mass;
        }

        public void
            SendGroupMsg<T>(Object message,
                bool sendAll) // Shorthand so I don't have to type out like 50 chars every time I do an IGC call
        {
            if (_group == 0 || sendAll)
            {
                IGC.SendBroadcastMessage("1", (T)message);
                IGC.SendBroadcastMessage("2", (T)message);
                IGC.SendBroadcastMessage("3", (T)message);
                IGC.SendBroadcastMessage("4", (T)message);
            }
            else
            {
                IGC.SendBroadcastMessage(_group.ToString(), (T)message);
            }
        }

        public char IndicateRun()
        {
            switch (_runIndicator)
            {
                case '|':
                    _runIndicator = '/';
                    break;
                case '/':
                    _runIndicator = '-';
                    break;
                case '-':
                    _runIndicator = '\\';
                    break;
                case '\\':
                    _runIndicator = '|';
                    break;
            }

            return _runIndicator;
        }

        void StartDeploy()
        {
            // Find connector on this grid
            List<IMyShipConnector> connectors = new List<IMyShipConnector>();
            GridTerminalSystem.GetBlocksOfType(connectors, c => c.CubeGrid.EntityId == GridId);

            _connector = connectors.FirstOrDefault(c => c.Status == MyShipConnectorStatus.Connected);

            if (_connector == null)
            {
                Echo("No connected connector found for deploy!");
                return;
            }

            // Store starting position
            _deployStartPos = Me.CubeGrid.GetPosition();

            // Calculate deploy direction (opposite of connector's forward direction)
            _deployDirection = -_connector.WorldMatrix.Forward;

            // Disconnect from connector
            _connector.Disconnect();

            // Set deploy state
            _isDeploying = true;
            _activated = true;
            Runtime.UpdateFrequency = UpdateFrequency.Update1;

            Echo($"Deploy started - moving {_deployDistance}m in direction: {_deployDirection}");
        }

        void UpdateDeploy()
        {
            if (!_isDeploying) return;

            OutText += $"Deploy Debug:\n";
            OutText += $"  Controller Pos: {_controllerPos}\n";
            OutText += $"  Is Valid: {IsValidVector(_controllerPos)}\n";
            OutText += $"  Is Zero: {_controllerPos == Vector3D.Zero}\n";

            // Calculate current distance from start position
            double distanceFromStart = Vector3D.Distance(_deployStartPos, Me.CubeGrid.GetPosition());

            if (distanceFromStart >= _deployDistance)
            {
                // Deploy distance reached - but don't immediately start normal operations
                _isDeploying = false;
                Echo("Deploy distance reached - waiting for controller data...");

                // Set to inactive and wait for proper controller data
                _activated = false;
                _mode = 1; // Set to wing mode for when we do activate

                // Stop all movement
                SetThrust(-1f, _allThrust, false);
                _gyros.Reset();

                // Let the normal IGC handler and activation logic take over
                // This ensures _controllerPos is properly set before moving
                return;
            }

            // Continue moving in deploy direction
            Vector3D targetPos = _deployStartPos + (_deployDirection * _deployDistance);
            Vector3D currentPos = Me.CubeGrid.GetPosition();
            Vector3D moveVector = targetPos - currentPos;

            // Use existing thrust control system
            Vector3D stopPosition = CalcStopPosition(-Me.CubeGrid.LinearVelocity, currentPos);
            if (!IsValidVector(stopPosition))
            {
                stopPosition = currentPos;
            }

            Vector3D thrustVector = stopPosition - targetPos;
            if (IsValidVector(thrustVector))
            {
                ThrustControl(thrustVector, _upThrust, _downThrust, _leftThrust, _rightThrust, _forwardThrust,
                    _backThrust);
            }

            // Point forward in deploy direction
            if (IsValidVector(_deployDirection))
            {
                _gyros.FaceVectors(_deployDirection, Me.WorldMatrix.Up);
            }

            OutText += $"DEPLOYING: {distanceFromStart:F1}m / {_deployDistance}m\n";
        }
    }
}