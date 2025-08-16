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
    public partial class Program : MyGridProgram
    {
        #region mdk preserve

        // Updated 11/14/23 //


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

        // Toggles debug mode. Outputs performance, but increases performance cost. Default [250]
        bool _debug = true;

        // Tag for docking components (connector and controller). Default "[AutoDock]"
        string _dockingTag = "[AutoDock]";

        /* PERFORMANCE SETTINGS */

        // Runtime threshold in milliseconds - operations throttle above this (PER DRONE)
        double _runtimeThreshold = 0.05;

        // Exponential moving average significance for runtime tracking
        const double RuntimeSignificance = 0.005;

        /* DRONE SETTINGS */

        // Set this to the grid's mass (in KG) IF there is no controller (cockpit, remote control) on the grid.
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

        // DON'T EDIT BELOW THIS LINE UNLESS YOU REALLY KNOW WHAT YOU'RE DOING //
        // OR YOU'RE ARISTEAS //
        //or you're oat :P//
        // I CAN'T STOP MYSELF //

        #endregion

        // In Development Version //
        //now with 2!!! contributers!!!!1!11!1!!!111!//
        // holy hell //

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

        #endregion

        // Core drone variables
        int _mode = 1;
        char _runIndicator = '|';
        long _gridEntityId; // Store our grid's entity ID for safety checks

        // API and helper classes
        PbApiWrapper _dApi;
        TargetingHelper _targeting;
        DebugAPI _d;
        DockingManager _dockingManager; // New docking system

        // Performance tracking
        double _averageRuntimeMs;

        // State variables
        bool _activated;
        Vector3D _centerOfGrid;
        IMyCockpit _cockpit;
        long _frame;
        MyDetectedEntityInfo _aiTarget;

        // Control systems - Made public for docking manager access
        public GyroControl _gyros;

        // Communication
        IMyBroadcastListener _myBroadcastListener;
        IMyBroadcastListener _positionListener;
        IMyBroadcastListener _velocityListener;
        IMyBroadcastListener _orientListener;
        IMyBroadcastListener _performanceListener;

        // Docking host communication (for controllers)
        IMyBroadcastListener _dockingPingListener;
        List<IMyShipConnector> _hostConnectors = new List<IMyShipConnector>();

        int _group = 1;
        List<long> _droneEntities = new List<long>();
        List<MyDetectedEntityInfo> _friendlies = new List<MyDetectedEntityInfo>();
        MyTuple<bool, int, int> _projectilesLockedOn = new MyTuple<bool, int, int>(false, 0, -1);

        public string OutText = "";
        public long GridId;

        #region drone-specific

        long _controlId = 0;
        long _lastControllerPing;
        static readonly double Cos45 = Math.Sqrt(2) / 2;
        int _formation;

        readonly Vector3D[][] _formationPresets = new Vector3D[][]
        {
            new[] // X formation
            {
                new Vector3D(_formDistance, 0, 0),
                new Vector3D(-_formDistance, 0, 0),
                new Vector3D(0, _formDistance, 0),
                new Vector3D(0, -_formDistance, 0),
                new Vector3D(Cos45 * _formDistance, Cos45 * _formDistance, 0),
                new Vector3D(Cos45 * _formDistance, -Cos45 * _formDistance, 0),
                new Vector3D(-Cos45 * _formDistance, Cos45 * _formDistance, 0),
                new Vector3D(-Cos45 * _formDistance, -Cos45 * _formDistance, 0),
                // Additional ring positions...
                new Vector3D(1.5 * _formDistance, 0, 0),
                new Vector3D(-1.5 * _formDistance, 0, 0),
                new Vector3D(0, 1.5 * _formDistance, 0),
                new Vector3D(0, -1.5 * _formDistance, 0),
                new Vector3D(1.5 * Cos45 * _formDistance, 1.5 * Cos45 * _formDistance, 0),
                new Vector3D(1.5 * Cos45 * _formDistance, -1.5 * Cos45 * _formDistance, 0),
                new Vector3D(-1.5 * Cos45 * _formDistance, 1.5 * Cos45 * _formDistance, 0),
                new Vector3D(-1.5 * Cos45 * _formDistance, -1.5 * Cos45 * _formDistance, 0),
                new Vector3D(3.0 * _formDistance, 0, 0),
                new Vector3D(-3.0 * _formDistance, 0, 0),
                new Vector3D(0, 3.0 * _formDistance, 0),
                new Vector3D(0, -3.0 * _formDistance, 0),
                new Vector3D(3.0 * Cos45 * _formDistance, 3.0 * Cos45 * _formDistance, 0),
                new Vector3D(3.0 * Cos45 * _formDistance, -3.0 * Cos45 * _formDistance, 0),
                new Vector3D(-3.0 * Cos45 * _formDistance, 3.0 * Cos45 * _formDistance, 0),
                new Vector3D(-3.0 * Cos45 * _formDistance, -3.0 * Cos45 * _formDistance, 0),
                new Vector3D(4.5 * _formDistance, 0, 0),
                new Vector3D(-4.5 * _formDistance, 0, 0),
                new Vector3D(0, 4.5 * _formDistance, 0),
                new Vector3D(0, -4.5 * _formDistance, 0),
                new Vector3D(4.5 * Cos45 * _formDistance, 4.5 * Cos45 * _formDistance, 0),
                new Vector3D(4.5 * Cos45 * _formDistance, -4.5 * Cos45 * _formDistance, 0),
                new Vector3D(-4.5 * Cos45 * _formDistance, 4.5 * Cos45 * _formDistance, 0),
                new Vector3D(-4.5 * Cos45 * _formDistance, -4.5 * Cos45 * _formDistance, 0)
            },
            new[] // Sphere formation
            {
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
            new[] // V formation
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

        // Combat and movement variables
        string _damageAmmo = "";
        string _healAmmo = "";
        bool _healMode;
        MatrixD _ctrlMatrix;
        bool _isFortified;
        bool _isIntegrity;
        ITerminalAction _toggleFort;
        ITerminalAction _toggleIntegrity;
        Vector3D _predictedTargetPos;
        Vector3D _controllerPos;
        Vector3D _anchorVelocity;
        Vector3D _closestCollision;
        Vector3D _ctrlTargetPos;
        double _totalRuntime = 0;
        Vector3D _resultPos;
        double _distanceTarget = 0;
        bool _hasABs = false;
        double _speed;
        int _id = -1;
        double _totalSwarmRuntime = 0;
        int _swarmDroneCount = 0;
        double _averageSwarmRuntimeMs;
        List<double> _currentFrameRuntimes = new List<double>();
        double _lastSwarmCalculation = 0;
        int _performanceCalculationFrame = 0;

        #endregion

        #region Blocks - Made public for docking manager access

        List<IMyTerminalBlock> _allABs = new List<IMyTerminalBlock>();
        List<IMyTerminalBlock> _forwardAb = new List<IMyTerminalBlock>();
        List<IMyTerminalBlock> _backAb = new List<IMyTerminalBlock>();
        List<IMyTerminalBlock> _upAb = new List<IMyTerminalBlock>();
        List<IMyTerminalBlock> _downAb = new List<IMyTerminalBlock>();
        List<IMyTerminalBlock> _leftAb = new List<IMyTerminalBlock>();
        List<IMyTerminalBlock> _rightAb = new List<IMyTerminalBlock>();
        List<IMyTerminalBlock> _fixedGuns = new List<IMyTerminalBlock>();
        List<IMyTerminalBlock> _flares = new List<IMyTerminalBlock>();
        public List<IMyThrust> _allThrust = new List<IMyThrust>();
        IMyRadioAntenna _antenna;

        IMyTerminalBlock _shieldController;
        IMyTerminalBlock _shieldModulator;

        double[] _thrustAmt = new double[6];
        public List<IMyThrust> _forwardThrust = new List<IMyThrust>();
        public List<IMyThrust> _backThrust = new List<IMyThrust>();
        public List<IMyThrust> _upThrust = new List<IMyThrust>();
        public List<IMyThrust> _downThrust = new List<IMyThrust>();
        public List<IMyThrust> _leftThrust = new List<IMyThrust>();
        public List<IMyThrust> _rightThrust = new List<IMyThrust>();

        List<IMyTextPanel> _outLcds = new List<IMyTextPanel>();

        Dictionary<string, int> _weaponMap = new Dictionary<string, int>();
        Dictionary<IMyTerminalBlock, Dictionary<string, int>> _cachedWeaponMaps =
            new Dictionary<IMyTerminalBlock, Dictionary<string, int>>();

        int _weaponMapUpdateFrame = 0;
        int _targetUpdateTimer;
        const int TargetUpdateInterval = 10;
        bool _hasShield;
        int _shieldCheckFrame = 0;

        Vector3D _cachedPredictedPos;
        int _predictedPosUpdateFrame = 0;

        Dictionary<string, Vector3D> _cachedAmmoLeadPositions = new Dictionary<string, Vector3D>();
        int _ammoLeadUpdateFrame = 0;
        string _cachedPrimaryAmmo = "";

        bool _pendingRecovery = false;

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

            // Store our grid entity ID for safety checks
            _gridEntityId = Me.CubeGrid.EntityId;
            GridId = _gridEntityId;

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

            // Initialize Docking Manager
            _dockingManager = new DockingManager(this, _dockingTag);
            _dockingManager.Initialize();
            Echo("Initialized Docking Manager");

            // Squares zoneRadius to avoid Vector3D.Distance() calls
            _zoneRadius *= _zoneRadius;

            // Force clear all WC-related caches
            _weaponMap.Clear();
            _cachedWeaponMaps.Clear();
            _cachedAmmoLeadPositions.Clear();
            _cachedPredictedPos = new Vector3D();
            _predictedTargetPos = new Vector3D();
            _aiTarget = new MyDetectedEntityInfo();

            if (!_isController)
            {
                if (Me.CustomData == "" || Me.CustomData.Length > 2) Me.CustomData = "1";
                int.TryParse(Me.CustomData, out _group);
                if (_group == -1)
                {
                    _isController = true;
                    _group = 0;
                }
            }
            else Me.CustomData = "-1";

            Echo($"Checked customdata for group ({_group})");

            // Init Whip's GPS Gyro Control
            _gyros = new GyroControl(this, Me, _kP, _kI, _kD, _lowerBound, _upperBound, _timeStep);
            Echo("Initialized Whip's Gyro Control");

            // Get cockpit for controller - ONLY on our grid
            GridTerminalSystem.GetBlocksOfType<IMyCockpit>(null, b =>
            {
                if (b.CubeGrid.EntityId == _gridEntityId)
                {
                    _cockpit = b;
                    return true;
                }
                return false;
            });
            Echo("Searched for cockpit on own grid " + (_cockpit == null ? "null" : _cockpit.CustomName));

            // Get shield components - ONLY on our grid
            List<IMyTerminalBlock> shieldControllers = new List<IMyTerminalBlock>();
            List<IMyTerminalBlock> shieldModulators = new List<IMyTerminalBlock>();
            bool hasEnhancer = false;
            
            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(shieldControllers, b =>
            {
                if (b.CubeGrid.EntityId != _gridEntityId) return false;
                if (!hasEnhancer) hasEnhancer = b.DefinitionDisplayNameText.Contains("Enhancer");
                return b.CustomName.Contains("Shield Controller");
            });
            Echo($"Located {shieldControllers.Count} shield controllers");
            
            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(shieldModulators,
                b => { return b.CubeGrid.EntityId == _gridEntityId && b.CustomName.Contains("Shield Modulator"); });
            Echo($"Located {shieldModulators.Count} shield modulators");
            
            if (_autoFortify) _autoFortify = hasEnhancer;

            // Set up shield actions
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

            // Auto-set mass if ship controller detected - ONLY on our grid
            GridTerminalSystem.GetBlocksOfType<IMyShipController>(null, b =>
            {
                if (b.CubeGrid.EntityId == _gridEntityId)
                {
                    _mass = b.CalculateShipMass().TotalMass;
                    return true;
                }
                return false;
            });
            Echo("Set grid mass to " + _mass);

            // Set antenna ranges - ONLY on our grid
            GridTerminalSystem.GetBlocksOfType<IMyRadioAntenna>(null, b =>
            {
                if (b.CubeGrid.EntityId == _gridEntityId)
                {
                    b.Radius = 25000;
                    _antenna = b;
                    return true;
                }
                return false;
            });
            Echo("Set antenna radii to 25km");

            // Get LCDs - ONLY on our grid
            if (_isController || _debug)
                GridTerminalSystem.GetBlocksOfType(_outLcds, b =>
                    b.CubeGrid.EntityId == _gridEntityId && b.CustomName.ToLower().Contains("rocketman"));

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

            // Learn ammo types
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

            RecalcABs();
            Echo($"Sorted {_allABs.Count} afterburners");

            // Get all thrust - ONLY on our grid
            GridTerminalSystem.GetBlocksOfType(_allThrust, t => t.CubeGrid.EntityId == _gridEntityId);
            Echo($"Found {_allThrust.Count} thrusters on own grid");

            RecalcThrust();
            Echo($"Sorted {_allThrust.Count} thrusters");

            // Reset controls
            _gyros.Reset();
            SetThrust(-1f, _allThrust, false);
            Echo("Reset thrust and gyro control");

            // Init IGC listeners
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

            // Initialize docking host listeners if we're a controller
            if (_isController)
            {
                _dockingPingListener = IGC.RegisterBroadcastListener("NETWORK_DISCOVERY_PING");
                _dockingPingListener.SetMessageCallback("DOCKING_IGC_Update");
                Echo("Inited docking host listener");
            }

            _antenna.HudText = "Awaiting " + (_isController ? "Drones!" : "Controller!");

            if (_isController)
            {
                SendGroupMsg<string>("r", true);
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

            _maxOffset = Math.Cos(_maxOffset / 57.2957795);
            Echo("Set maxOffset to " + _maxOffset);

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

            _frame = 0;
            Echo("Successfully initialized as a " + (_isController ? "controller." : "drone.") + "");
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

            // Handle docking and recovery commands
            if (!string.IsNullOrEmpty(argument))
            {
                switch (argument.ToLower())
                {
                    case "dock":
                    case "autodock":
                        if (!_isController) // Only drones can initiate docking
                        {
                            if (_dockingManager.StartDocking())
                            {
                                Echo("Starting docking sequence...");
                                Runtime.UpdateFrequency = UpdateFrequency.Update10;
                            }
                            else
                            {
                                Echo("Failed to start docking - check connector and controller tags");
                            }
                        }
                        else
                        {
                            Echo("Controllers cannot initiate docking - they receive docking requests");
                        }
                        return;
                    case "abortdock":
                    case "stopdock":
                        _dockingManager.AbortDocking();
                        Echo("Docking aborted");
                        return;
                    case "refreshdocks":
                        if (_isController)
                        {
                            InitializeDockingHost();
                            Echo("Docking connectors refreshed");
                        }
                        return;
                    case "DOCKING_IGC_Update":
                        if (_isController)
                        {
                            ProcessDockingHostMessages();
                        }
                        return;
                }
            }

            // Handle recovery commands
            if (!string.IsNullOrEmpty(argument) &&
                (argument == "recover" || argument == "recycle" || argument == "reset"))
            {
                if (_isController)
                {
                    SendGroupMsg<string>("recover", true);
                    IGC.SendBroadcastMessage("-1", "recover");
                }
                RecoverDrone();
                return;
            }
            else if ((!_isController || _mode == 1) && !_activated && !_dockingManager.IsDocking)
            {
                Runtime.UpdateFrequency = UpdateFrequency.Update100;
            }

            _d.RemoveAll();

            // Update docking system - NO THROTTLING for docking operations
            if (_dockingManager.IsDocking)
            {
                _dockingManager.Update();
                OutText += _dockingManager.GetStatusText() + "\n";
                
                // Check if docking completed successfully
                if (!_dockingManager.IsDocking && _mode == 3)
                {
                    // Docking finished - drone becomes inactive until given new orders
                    _activated = false;
                    _mode = 1; // Reset to wingman mode for future commands
                    _antenna.HudText = _id.ToString() + " | DOCKED";
                    Runtime.UpdateFrequency = UpdateFrequency.Update100;
                    Echo("Docking complete - drone now inactive");
                }
                
                // Always update LCDs during docking for real-time feedback
                foreach (var l in _outLcds)
                    l.WriteText(OutText);
                
                OutText = "";
                _frame++;
                return;
            }

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
            OutText += $"M{_mode} : G{_group} : ID{_id} {(_activated ? "ACTIVE" : "INACTIVE")} {(_dockingManager.IsDocking ? "DOCKING" : "")} {IndicateRun()}\n";
            OutText += $"Runtime: {Runtime.LastRunTimeMs:F2}ms | Avg: {_averageRuntimeMs:F2}ms | Limit: {_runtimeThreshold}ms\n";
            OutText += $"\nRocketman Drone Manager\n-------------------------\n{(_isController ? $"Controlling {_droneEntities.Count} drone(s)" : "Drone Mode")}\n";

            // If ID unset and is not controller, ping controller for ID.
            if (_id == -1 && !_isController)
            {
                _activated = false;
                IGC.SendBroadcastMessage("-1", "e" + _gridEntityId); // Use our stored grid ID
                return;
            }

            if (_activated)
            {
                try
                {
                    _centerOfGrid = Me.CubeGrid.GetPosition();

                    bool isThrottled = _averageRuntimeMs >= _runtimeThreshold;
                    int throttleFrames = isThrottled ? 6 : 1;

                    if (!isThrottled || (_frame % throttleFrames) == 0)
                    {
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
                            if (!currentTarget.IsEmpty())
                            {
                                _aiTarget = currentTarget;
                            }
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
                            UpdateSwarmPerformanceAverage();
                            // Process docking host messages for controllers
                            ProcessDockingHostMessages();
                        }
                    }
                    
                    if (!_isController)
                    {
                        RunActiveDrone();
                    }

                    if (isThrottled)
                    {
                        OutText += $"THROTTLED ({throttleFrames}x slower)\n";
                    }

                    _errorCounter = 0;
                }
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

            if (_debug)
            {
                PrintDebugText();
                if (_averageRuntimeMs < _runtimeThreshold * 0.5)
                    DrawLeadDebugs();
            }

            // Check for WeaponCore issues
            if (_activated && !_doCcrp && _fixedGuns.Count > 0 &&
                _frame % (_averageRuntimeMs > _runtimeThreshold ? 120 : 60) == 0)
            {
                Echo("WeaponCore not active but weapons present - attempting recovery");
                ReinitializeWeaponCore();
            }

            // Auto-detect copied drones
            if (_activated && _id != -1 && !_isController &&
                _frame % (_averageRuntimeMs > _runtimeThreshold ? 600 : 300) == 0)
            {
                if (GridId != _gridEntityId)
                {
                    Echo("Grid ID mismatch detected - likely a copied drone!");
                    Echo($"Stored ID: {GridId}, Actual ID: {_gridEntityId}");
                    RecoverDrone();
                    return;
                }
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
            Echo("Initiating drone recovery...");

            _id = -1;
            _activated = false;
            _lastControllerPing = 0;
            _aiTarget = new MyDetectedEntityInfo();
            _cachedPredictedPos = new Vector3D();
            _predictedTargetPos = new Vector3D();
            _droneEntities.Clear();
            _friendlies.Clear();

            if (_gyros != null) _gyros.Reset();
            SetThrust(-1f, _allThrust, false);

            ReinitializeWeaponCore();

            // Update grid tracking with current entity ID
            _gridEntityId = Me.CubeGrid.EntityId;
            GridId = _gridEntityId;

            // Reinitialize communication
            _myBroadcastListener = IGC.RegisterBroadcastListener(_isController ? "-1" : _group.ToString());
            _positionListener = IGC.RegisterBroadcastListener("pos" + (_multipleControllers ? _group.ToString() : ""));
            _velocityListener = IGC.RegisterBroadcastListener("vel" + (_multipleControllers ? _group.ToString() : ""));
            _orientListener = IGC.RegisterBroadcastListener("ori" + (_multipleControllers ? _group.ToString() : ""));
            _performanceListener = IGC.RegisterBroadcastListener("per");

            if (_antenna != null) _antenna.HudText = "Recovery Complete - Ready for Combat!";

            IGC.SendBroadcastMessage("-1", "e" + _gridEntityId);
            _frame = 0;

            Echo("Drone recovery completed - Grid ID: " + GridId);
        }

        void ReinitializeWeaponCore()
        {
            Echo("Reinitializing WeaponCore systems...");

            _fixedGuns.Clear();
            _allABs.Clear();
            _flares.Clear();
            _weaponMap.Clear();
            _cachedWeaponMaps.Clear();
            _cachedAmmoLeadPositions.Clear();

            _doCcrp = true;

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

            int retryCount = 0;
            bool wcInitSuccess = false;

            while (retryCount < 3 && !wcInitSuccess)
            {
                try
                {
                    _targeting.GetWeapons(_fixedGuns, _gunGroupName);
                    Echo($"Found {_fixedGuns.Count} weapons");

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
                        wcInitSuccess = true;
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
                        wcInitSuccess = true;
                    }
                }
            }

            try
            {
                _targeting.GetWeapons(_allABs, _abGroupName);
                Echo($"Found {_allABs.Count} afterburners");
                RecalcABs();

                _targeting.GetWeapons(_flares, _flareGroupName);
                Echo($"Found {_flares.Count} flares");

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

        // Docking Host Functionality (for controllers)
        void InitializeDockingHost()
        {
            if (!_isController) return;

            _hostConnectors.Clear();
            
            // Get all connectors with the docking tag on our grid only
            GridTerminalSystem.GetBlocksOfType<IMyShipConnector>(_hostConnectors, 
                block => block.CustomName.Contains(_dockingTag) && 
                         block.CubeGrid.EntityId == _gridEntityId);

            Echo($"Found {_hostConnectors.Count} host docking connectors");

            // Disable connectors that are not locked or in proximity to free them up
            foreach (IMyShipConnector connector in _hostConnectors)
            {
                MyShipConnectorStatus status = connector.Status;
                if (status == MyShipConnectorStatus.Unconnected || 
                    status == MyShipConnectorStatus.Connectable)
                {
                    connector.Enabled = false;
                }
            }
        }

        void ProcessDockingHostMessages()
        {
            if (!_isController) return;

            // Process incoming PINGS for docking discovery
            while (_dockingPingListener.HasPendingMessage)
            {
                MyIGCMessage message = _dockingPingListener.AcceptMessage();
                if (message.Tag == "NETWORK_DISCOVERY_PING" && message.Data.ToString() == "PING")
                {
                    // Don't respond to our own messages or messages from same grid
                    if (message.Source == IGC.Me || AreBlocksOnSameGrid(message.Source, Me)) 
                        continue;

                    OutText += $"Docking ping received from: {message.Source}\n";

                    // Send response with our grid info
                    var responsePacket = new MyTuple<string, long, Vector3D>(
                        Me.CubeGrid.CustomName, 
                        IGC.Me, 
                        Me.GetPosition()
                    );
                    IGC.SendUnicastMessage(message.Source, "NETWORK_DISCOVERY_PONG", responsePacket);
                    
                    // Refresh connectors when someone is looking for docking
                    InitializeDockingHost();
                }
            }

            // Process docking requests through unicast
            while (IGC.UnicastListener.HasPendingMessage)
            {
                MyIGCMessage message = IGC.UnicastListener.AcceptMessage();
                if (message.Tag == "NETWORK_DOCKINGREQUEST")
                {
                    Vector3D requestingShipPosition = (Vector3D)message.Data;
                    IMyShipConnector assignedConnector = GetAvailableHostConnector(requestingShipPosition);
                    
                    if (assignedConnector != null)
                    {
                        OutText += $"Assigned connector {assignedConnector.CustomName} to {message.Source}\n";
                        
                        // Get connector orientation info
                        Vector3D connectorPos = assignedConnector.GetPosition();
                        MatrixD worldMatrix = assignedConnector.WorldMatrix;
                        Vector3D connectorUp = worldMatrix.Up;
                        Vector3D connectorForward = worldMatrix.Forward;

                        // Send success response
                        var responsePacket = new MyTuple<bool, Vector3D, Vector3D, Vector3D>(
                            true, connectorPos, connectorUp, connectorForward
                        );
                        IGC.SendUnicastMessage(message.Source, "NETWORK_DOCKINGREQUEST", responsePacket);
                    }
                    else
                    {
                        OutText += $"No available connectors for {message.Source}\n";
                        
                        // Send failure response
                        var responsePacket = new MyTuple<bool, Vector3D, Vector3D, Vector3D>(
                            false, Vector3D.Zero, Vector3D.Zero, Vector3D.Zero
                        );
                        IGC.SendUnicastMessage(message.Source, "NETWORK_DOCKINGREQUEST", responsePacket);
                    }
                }
            }
        }

        IMyShipConnector GetAvailableHostConnector(Vector3D requestingShipPosition)
        {
            if (!_isController) return null;

            List<IMyShipConnector> availableConnectors = new List<IMyShipConnector>();

            // Find all disabled (available) connectors on our grid
            foreach (IMyShipConnector connector in _hostConnectors)
            {
                // Only consider connectors on our grid
                if (connector.CubeGrid.EntityId != _gridEntityId) continue;
                
                MyShipConnectorStatus status = connector.Status;
                if (!connector.Enabled && 
                    (status == MyShipConnectorStatus.Unconnected || 
                     status == MyShipConnectorStatus.Connectable))
                {
                    availableConnectors.Add(connector);
                }
            }

            if (availableConnectors.Count == 0)
                return null;

            // Find the closest available connector
            IMyShipConnector closestConnector = null;
            double closestDistance = double.MaxValue;

            foreach (IMyShipConnector connector in availableConnectors)
            {
                double distance = Vector3D.Distance(connector.GetPosition(), requestingShipPosition);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestConnector = connector;
                }
            }

            // Enable the selected connector to reserve it
            if (closestConnector != null)
            {
                closestConnector.Enabled = true;
                Echo($"Reserved connector: {closestConnector.CustomName}");
            }

            return closestConnector;
        }

        bool AreBlocksOnSameGrid(long blockId1, IMyTerminalBlock block2)
        {
            IMyTerminalBlock block1 = GridTerminalSystem.GetBlockWithId(blockId1);

            if (block1 == null || block2 == null)
                return false;

            return block1.CubeGrid.EntityId == block2.CubeGrid.EntityId;
        }

        // Make CalcStopPosition and ThrustControl public for docking manager
        public Vector3D CalcStopPosition(Vector3D velocity, Vector3D gridCenter)
        {
            if (!IsValidVector(velocity) || !IsValidVector(gridCenter))
            {
                return gridCenter;
            }

            Vector3D accel = new Vector3D(_thrustAmt[4], _thrustAmt[2], -_thrustAmt[0]) / _mass;
            Vector3D accelR = new Vector3D(-_thrustAmt[5], -_thrustAmt[3], _thrustAmt[1]) / _mass;

            if (!IsValidVector(accel) || !IsValidVector(accelR))
            {
                return gridCenter;
            }

            Vector3D rVelocity = Vector3D.Rotate(velocity, Me.WorldMatrix);

            if (!IsValidVector(rVelocity))
            {
                return gridCenter;
            }

            Vector3D timeToStop = new Vector3D(
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

            Vector3D result = gridCenter - (velocity * timeToStop.Length()) / 2;

            if (!IsValidVector(result))
            {
                return gridCenter;
            }

            return result;
        }

        public void ThrustControl(Vector3D relPos, List<IMyThrust> up, List<IMyThrust> down, List<IMyThrust> left,
            List<IMyThrust> right, List<IMyThrust> forward, List<IMyThrust> back)
        {
            Vector3D tMove = relPos;
            MatrixD wm = Me.WorldMatrix;
            tMove = new Vector3D(Vector3D.Dot(tMove, wm.Right), Vector3D.Dot(tMove, wm.Forward),
                Vector3D.Dot(tMove, wm.Up));

            SetThrust(-tMove.X, right, true);
            SetThrust(tMove.X, left, true);
            SetThrust(-tMove.Y, forward, true);
            SetThrust(tMove.Y, back, true);
            SetThrust(-tMove.Z, up, true);
            SetThrust(tMove.Z, down, true);

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

        public void SetThrust(double pct, List<IMyThrust> thrusters, bool disable)
        {
            float percent = (float)pct;
            if (thrusters.Count == 0) return;
            foreach (var thrust in thrusters)
            {
                thrust.ThrustOverridePercentage = percent;
            }
        }

        // Helper methods
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

        private void ActiveDroneFrame2()
        {
            _resultPos = _aiTarget.IsEmpty() ? _ctrlTargetPos : _predictedTargetPos;

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

            int performanceMultiplier = isThrottled ? 4 : 1;

            if (_frame % (60 * performanceMultiplier) == 0)
            {
                if (!_healMode && (_mode == 0 || _autoTarget))
                    _targeting.SetTarget(_targeting.GetClosestTarget());
                else if (_healMode && _autoTarget)
                    _aiTarget = _friendlies[0];

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
                    GridTerminalSystem.GetBlocksOfType(_allThrust, t => t.CubeGrid.EntityId == _gridEntityId);
                    RecalcThrust();
                    OutText += $"Recalculated thrust with {_allThrust.Count} thrusters";
                }

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

            if (_frame % (300 * performanceMultiplier * 2) == 0)
            {
                _cachedPredictedPos = new Vector3D();
                _cachedAmmoLeadPositions.Clear();
                _weaponMap.Clear();
            }
        }

        void RunActiveDrone()
        {
            OutText += "Locked onto " + _aiTarget.Name + "\n";
            if (_frame % 2 == 0) ActiveDroneFrame2();

            bool isThrottled = _averageRuntimeMs >= _runtimeThreshold;

            Vector3D aimPoint = new Vector3D();
            Vector3D aimDirection = Me.WorldMatrix.Forward;
            bool hasValidAimPoint = false;

            if (!_aiTarget.IsEmpty())
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
                    if (_frame % ammoUpdateInterval == 0)
                    {
                        _cachedPrimaryAmmo = wcTargeting.wAPI.GetActiveAmmo(_fixedGuns.First(), _weaponMap.First().Value);
                    }

                    if (hasValidAimPoint)
                    {
                        _predictedTargetPos = _cachedPredictedPos;
                        aimPoint = _predictedTargetPos;
                    }
                }

                if (!hasValidAimPoint && IsValidVector(_aiTarget.Position))
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
            else
            {
                if (IsValidMatrix(_ctrlMatrix))
                {
                    aimDirection = _ctrlMatrix.Forward;
                }
                else
                {
                    aimDirection = Me.WorldMatrix.Forward;
                }
            }

            Vector3D moveTo = new Vector3D();
            Vector3D stopPosition = CalcStopPosition(-Me.CubeGrid.LinearVelocity, _centerOfGrid);

            if (!IsValidVector(stopPosition))
            {
                stopPosition = _centerOfGrid;
            }

            if (!_aiTarget.IsEmpty() && !isThrottled && IsValidVector(aimPoint))
            {
                _d.DrawLine(_centerOfGrid, aimPoint, Color.Red, 0.1f);
            }

            if (!isThrottled && IsValidVector(stopPosition))
                _d.DrawGPS("Stop Position", stopPosition);

            switch (_mode)
            {
                case 0:
                    if (IsValidVector(aimDirection))
                    {
                        _gyros.FaceVectors(aimDirection, Me.WorldMatrix.Up);
                    }

                    if (!_aiTarget.IsEmpty() && IsValidVector(_aiTarget.Position))
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
                        Vector3D formationOffset = Vector3D.Rotate(_formationPresets[_formation][_id], _ctrlMatrix);
                        if (IsValidVector(formationOffset) && IsValidVector(_controllerPos))
                        {
                            moveTo = _controllerPos + formationOffset;
                        }
                        else
                        {
                            moveTo = _centerOfGrid;
                        }
                    }

                    if (!isThrottled && IsValidVector(moveTo))
                        _d.DrawLine(_centerOfGrid, moveTo, Color.Blue, 0.1f);

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
                            if (wCTargeting.wAPI.GetActiveAmmo(wep, 0) == _damageAmmo) break;
                            wep.SetValue<Int64>("WC_PickAmmo", 0);
                        }
                    }

                    Vector3D formationAimDirection;
                    if (dSq > _formDistance * _formDistance * 4 || _healMode)
                    {
                        Vector3D toController = _controllerPos - _centerOfGrid;
                        if (IsValidVector(toController) && toController.LengthSquared() > 0.01)
                        {
                            formationAimDirection = Vector3D.Normalize(toController);
                        }
                        else
                        {
                            formationAimDirection = Me.WorldMatrix.Forward;
                        }
                    }
                    else if (!_aiTarget.IsEmpty() && IsValidVector(aimDirection))
                    {
                        formationAimDirection = aimDirection;
                    }
                    else if (IsValidMatrix(_ctrlMatrix))
                    {
                        formationAimDirection = _ctrlMatrix.Forward;
                    }
                    else
                    {
                        formationAimDirection = Me.WorldMatrix.Forward;
                    }

                    if (!IsValidVector(formationAimDirection))
                    {
                        formationAimDirection = Me.WorldMatrix.Forward;
                    }

                    if (!_aiTarget.IsEmpty() && !_healMode && IsValidVector(aimDirection))
                    {
                        _gyros.FaceVectors(aimDirection, Me.WorldMatrix.Up);
                    }
                    else
                    {
                        _gyros.FaceVectors(formationAimDirection, Me.WorldMatrix.Up);
                    }

                    Vector3D formationPos = Vector3D.Rotate(_formationPresets[_formation][_id], _ctrlMatrix);
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
                        if (IsValidVector(_controllerPos))
                            _d.DrawLine(_centerOfGrid, _controllerPos, Color.Green, 0.1f);
                        if (IsValidVector(moveTo))
                        {
                            _d.DrawLine(_centerOfGrid, moveTo, Color.Blue, 0.1f);
                            _d.DrawGPS("Drone Position", moveTo);
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

                case 2:
                    if (IsValidVector(aimDirection))
                    {
                        _gyros.FaceVectors(aimDirection, Me.WorldMatrix.Up);
                    }

                    Vector3D fortifyPos = _formationPresets[_formation][_id];
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

        private void PrintDebugText()
        {
            OutText += $"{Runtime.CurrentInstructionCount} instructions @ {Runtime.LastRunTimeMs:F2}ms\n";
            OutText += $"Avg Runtime: {_averageRuntimeMs:F2}ms\n";

            if (_isController)
            {
                OutText += $"Swarm Avg: {_averageSwarmRuntimeMs:F2}ms | Drones: {_droneEntities.Count}\n";
                OutText += $"Controller: {(Runtime.LastRunTimeMs / 16.67 * 100):F1}% | Swarm: {(_averageSwarmRuntimeMs / 16.67 * 100):F1}%\n";
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
            bool wasMessageRecieved = false;
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

        private void UpdateSwarmPerformanceAverage()
        {
            if (!_isController || _currentFrameRuntimes.Count == 0) return;

            double currentSwarmAverage = _currentFrameRuntimes.Sum();
            _averageSwarmRuntimeMs = currentSwarmAverage;
            _currentFrameRuntimes.Clear();
        }

        public void AutoIntegrity()
        {
            if (_autoIntegrity && _dApi.GridHasShield(Me.CubeGrid) && !_isController)
            {
                try
                {
                    if (_distanceTarget < 40000 && !_isIntegrity)
                    {
                        _toggleIntegrity.Apply(_shieldModulator);
                        _isIntegrity = true;
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

                bool isThrottled = _averageRuntimeMs >= _runtimeThreshold;
                if (!isThrottled)
                    _d.DrawLine(_centerOfGrid, _centerOfGrid + m.Up * 100, Color.Blue, 0.1f);

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
                bool isLinedUp = Vector3D.Normalize(_controllerPos - _centerOfGrid).Dot(Me.WorldMatrix.Forward) > _maxOffset;
                foreach (var weapon in _fixedGuns)
                {
                    if (!isThrottled)
                        _d.DrawLine(_centerOfGrid, Me.WorldMatrix.Forward * _targeting.GetMaxRange(weapon) + _centerOfGrid,
                            Color.White, 0.5f);
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
                                Vector3D? predictedPos = wcTargeting.wAPI.GetPredictedTargetPosition(weapon, _aiTarget.EntityId, weaponId);
                                if (predictedPos.HasValue)
                                {
                                    _cachedAmmoLeadPositions[activeAmmo] = predictedPos.Value;
                                }
                            }
                        }
                    }
                }

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

            OutText += $"ParseCommands: '{argument}' from {updateSource}\n";

            switch (argument)
            {
                case "main":
                    _mode = 0;
                    _healMode = false;
                    if (_isController)
                        SendGroupMsg<string>("main", false);
                    _antenna.HudText = _id.ToString() + " | " + _mode.ToString() + _group.ToString();
                    OutText += "Switched to MAIN mode\n";
                    return;
                case "wing":
                    _mode = 1;
                    _healMode = false;
                    if (_isController)
                        SendGroupMsg<string>("wing", false);
                    _antenna.HudText = _id.ToString() + " | " + _mode.ToString() + _group.ToString();
                    OutText += "Switched to WINGMAN mode\n";
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
                    OutText += "Switched to FORTIFY mode\n";
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
                        IGC.SendBroadcastMessage("ori", _cockpit.IsFunctional ? _cockpit.WorldMatrix : Me.WorldMatrix);
                    }
                    OutText += "STARTED - Drone activated\n";
                    return;
                case "group":
                    if (_group < 4) _group++;
                    else _group = 0;
                    _antenna.HudText = _id.ToString() + " | " + _mode.ToString() + _group.ToString();
                    OutText += $"Changed to group {_group}\n";
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
                    OutText += "STOPPED - Drone deactivated\n";
                    return;
                case "dock":
                case "autodock":
                    OutText += $"DOCK command - isController: {_isController}\n";
                    if (_isController)
                    {
                        // Controller sends dock command to all drones
                        SendGroupMsg<string>("dock", false);
                        OutText += "Sent dock command to all drones\n";
                    }
                    else
                    {
                        OutText += "Drone processing dock command...\n";
                        // Drone switches to docking mode and starts docking
                        _mode = 3; // Docking mode
                        _healMode = false;
                        _antenna.HudText = _id.ToString() + " | " + _mode.ToString() + _group.ToString() + " DOCKING";
                        OutText += $"Switched to DOCKING mode (M{_mode})\n";
                        
                        if (_dockingManager.StartDocking())
                        {
                            OutText += "SUCCESS: Docking sequence started\n";
                            Runtime.UpdateFrequency = UpdateFrequency.Update10;
                        }
                        else
                        {
                            OutText += "FAILED: Could not start docking\n";
                            _mode = 1; // Fall back to wingman mode
                            _antenna.HudText = _id.ToString() + " | " + _mode.ToString() + _group.ToString();
                        }
                    }
                    return;
                case "abortdock":
                case "stopdock":
                    OutText += $"ABORT DOCK command - isController: {_isController}\n";
                    if (_isController)
                    {
                        // Controller sends abort dock command to all drones
                        SendGroupMsg<string>("abortdock", false);
                        OutText += "Sent abort dock command to all drones\n";
                    }
                    else
                    {
                        // Drone aborts docking and returns to wingman mode
                        _dockingManager.AbortDocking();
                        _mode = 1; // Return to wingman mode
                        _antenna.HudText = _id.ToString() + " | " + _mode.ToString() + _group.ToString();
                        OutText += "Docking aborted - returning to wingman mode\n";
                    }
                    return;
                case "refreshdocks":
                    if (_isController)
                    {
                        InitializeDockingHost();
                        OutText += "Docking connectors refreshed\n";
                        SendGroupMsg<string>("refreshdocks", false);
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
                    OutText += "Switched to HEAL mode\n";
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
                        Echo("Learned heal ammo " + _healAmmo);
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

            switch (argument[0])
            {
                case 'i':
                    if (!_isController && long.Parse(argument.Substring(3)) == _gridEntityId)
                    {
                        _id = int.Parse(argument.Substring(1, 2));
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
                            SendGroupMsg<string>("i" + (i < 10 ? "0" + i.ToString() : i.ToString()) + _droneEntities[i], true);
                        }
                        _antenna.HudText = _id.ToString() + " | " + _mode.ToString() + _group.ToString();

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
                case 'c':
                    if (!_isController)
                    {
                        long.TryParse(argument.Substring(1), out _controlId);
                    }
                    break;
            }
        }

        public void RecalcThrust()
        {
            // Clear existing thrust lists
            _forwardThrust.Clear();
            _backThrust.Clear();
            _upThrust.Clear();
            _downThrust.Clear();
            _leftThrust.Clear();
            _rightThrust.Clear();
            
            // Reset thrust amounts
            for (int i = 0; i < 6; i++)
                _thrustAmt[i] = 0;

            foreach (var thrust in _allThrust)
            {
                // Only process thrusters on our grid
                if (thrust.CubeGrid.EntityId != _gridEntityId) continue;

                if (thrust.Orientation.Forward == Base6Directions.GetOppositeDirection(Me.Orientation.Forward))
                {
                    _forwardThrust.Add(thrust);
                    _thrustAmt[0] += thrust.MaxEffectiveThrust;
                }
                else if (thrust.Orientation.Forward == Base6Directions.GetOppositeDirection(Me.Orientation.Up))
                {
                    _upThrust.Add(thrust);
                    _thrustAmt[2] += thrust.MaxEffectiveThrust;
                }
                else if (thrust.Orientation.Forward == Base6Directions.GetOppositeDirection(Me.Orientation.Left))
                {
                    _leftThrust.Add(thrust);
                    _thrustAmt[5] += thrust.MaxEffectiveThrust;
                }
                else if (thrust.Orientation.Forward == Me.Orientation.Forward)
                {
                    _backThrust.Add(thrust);
                    _thrustAmt[1] += thrust.MaxEffectiveThrust;
                }
                else if (thrust.Orientation.Forward == Me.Orientation.Up)
                {
                    _downThrust.Add(thrust);
                    _thrustAmt[3] += thrust.MaxEffectiveThrust;
                }
                else if (thrust.Orientation.Forward == Me.Orientation.Left)
                {
                    _rightThrust.Add(thrust);
                    _thrustAmt[4] += thrust.MaxEffectiveThrust;
                }
            }
        }

        public void RecalcABs()
        {
            // Clear existing AB lists
            _forwardAb.Clear();
            _backAb.Clear();
            _upAb.Clear();
            _downAb.Clear();
            _leftAb.Clear();
            _rightAb.Clear();

            foreach (var ab in _allABs)
            {
                // Only process afterburners on our grid
                if (ab.CubeGrid.EntityId != _gridEntityId) continue;

                if (ab.Orientation.Forward == Base6Directions.GetOppositeDirection(Me.Orientation.Forward))
                {
                    _forwardAb.Add(ab);
                }
                else if (ab.Orientation.Forward == Base6Directions.GetOppositeDirection(Me.Orientation.Up))
                {
                    _upAb.Add(ab);
                }
                else if (ab.Orientation.Forward == Base6Directions.GetOppositeDirection(Me.Orientation.Left))
                {
                    _leftAb.Add(ab);
                }
                else if (ab.Orientation.Forward == Me.Orientation.Forward)
                {
                    _backAb.Add(ab);
                }
                else if (ab.Orientation.Forward == Me.Orientation.Up)
                {
                    _downAb.Add(ab);
                }
                else if (ab.Orientation.Forward == Me.Orientation.Left)
                {
                    _rightAb.Add(ab);
                }
            }

            _hasABs = _allABs.Count > 0;
        }

        public double RoundPlaces(double d, int place)
        {
            return ((int)(d * Math.Pow(10, place))) / Math.Pow(10, place);
        }

        public double TwrCalc(int dir)
        {
            return _thrustAmt[dir] / _mass;
        }

        public void SendGroupMsg<T>(Object message, bool sendAll)
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
    }
}