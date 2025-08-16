using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRage;
using VRageMath;

namespace IngameScript
{
    public class DockingManager
    {
        private Program _program;
        private long _gridEntityId;
        
        // Docking constants
        private const string PING_CHANNEL = "NETWORK_DISCOVERY_PING";
        private const string PONG_TAG = "NETWORK_DISCOVERY_PONG";
        private const string DOCKREQUEST_TAG = "NETWORK_DOCKINGREQUEST";
        
        // State machine
        public enum DockingState
        {
            Inactive = -1,
            Error = -2,
            Setup = -3,
            Ping = -4,
            WaitingForResponse = 0,
            RequestingConnector = 1,
            WaitingForAssignment = 2,
            Docking = 3,
            Complete = 4,
            Aborted = 5
        }
        
        private DockingState _currentState = DockingState.Inactive;
        
        // Communication
        private IMyBroadcastListener _broadcastListener;
        private IMyUnicastListener _unicastListener;
        
        // Target information
        private long _targetGrid = 0;
        private Vector3D _targetConnectorPosition;
        private Vector3D _targetConnectorUp;
        private Vector3D _targetConnectorForward;
        
        // Discovered grids
        private Dictionary<long, string> _discoveredGridsCustomName = new Dictionary<long, string>();
        private Dictionary<long, Vector3D> _discoveredGridsPosition = new Dictionary<long, Vector3D>();
        
        // Ship components (references to existing drone components)
        private IMyShipController _controller;
        private IMyShipConnector _shipConnector;
        private string _dockingTag;
        
        // PID values for docking approach
        private double _thrustersP = 1.0;
        private double _thrustersD = 0.2;
        
        public bool IsDocking => _currentState != DockingState.Inactive && _currentState != DockingState.Complete && _currentState != DockingState.Aborted;
        public DockingState CurrentState => _currentState;
        
        public DockingManager(Program program, string dockingTag = "[AutoDock]")
        {
            _program = program;
            _gridEntityId = program.Me.CubeGrid.EntityId;
            _dockingTag = dockingTag;
        }
        
        public void Initialize()
        {
            // Set up IGC listeners
            _broadcastListener = _program.IGC.RegisterBroadcastListener(PING_CHANNEL);
            _unicastListener = _program.IGC.UnicastListener;
            
            // Find ship components on our grid only
            FindShipComponents();
        }
        
        private void FindShipComponents()
        {
            _program.OutText += $"Searching for '{_dockingTag}' on grid {_gridEntityId}\n";
            
            // Find controller with docking tag on our grid
            List<IMyShipController> controllers = new List<IMyShipController>();
            _program.GridTerminalSystem.GetBlocksOfType<IMyShipController>(controllers, 
                block => block.CustomName.Contains(_dockingTag) && block.CubeGrid.EntityId == _gridEntityId);
            
            _program.OutText += $"Controllers with tag: {controllers.Count}\n";
            
            if (controllers.Count > 0)
            {
                _controller = controllers[0];
                _program.OutText += $"Using controller: {_controller.CustomName}\n";
            }
            else
            {
                // Fall back to any controller on our grid (use existing cockpit if available)
                _program.GridTerminalSystem.GetBlocksOfType<IMyShipController>(controllers,
                    block => block.CubeGrid.EntityId == _gridEntityId);
                if (controllers.Count > 0)
                {
                    _controller = controllers[0];
                    _program.OutText += $"Fallback controller: {_controller.CustomName}\n";
                }
                else
                {
                    _program.OutText += "ERROR: No controllers found!\n";
                }
            }
            
            // Find connector with docking tag on our grid
            List<IMyShipConnector> connectors = new List<IMyShipConnector>();
            _program.GridTerminalSystem.GetBlocksOfType<IMyShipConnector>(connectors,
                block => block.CustomName.Contains(_dockingTag) && block.CubeGrid.EntityId == _gridEntityId);
            
            _program.OutText += $"Connectors with tag: {connectors.Count}\n";
            
            if (connectors.Count > 0)
            {
                _shipConnector = connectors[0];
                _program.OutText += $"Using connector: {_shipConnector.CustomName}\n";
            }
            else
            {
                _program.OutText += "ERROR: No connectors with tag!\n";
                
                // List all connectors on grid for debugging
                List<IMyShipConnector> allConnectors = new List<IMyShipConnector>();
                _program.GridTerminalSystem.GetBlocksOfType<IMyShipConnector>(allConnectors,
                    block => block.CubeGrid.EntityId == _gridEntityId);
                _program.OutText += $"All connectors on grid: {allConnectors.Count}\n";
                foreach (var conn in allConnectors)
                {
                    _program.OutText += $"  - {conn.CustomName}\n";
                }
            }
        }
        
        public bool StartDocking()
        {
            _program.OutText += "DockingManager.StartDocking() called\n";
            
            if (_controller == null || _shipConnector == null)
            {
                _program.OutText += $"Docking failed: Controller: {(_controller == null ? "NULL" : "OK")}, Connector: {(_shipConnector == null ? "NULL" : "OK")}\n";
                _program.OutText += $"Looking for tag: {_dockingTag}\n";
                return false;
            }
            
            _program.OutText += "Found required components, starting docking...\n";
            _discoveredGridsCustomName.Clear();
            _discoveredGridsPosition.Clear();
            _targetGrid = 0;
            _currentState = DockingState.Setup;
            
            return true;
        }
        
        public void AbortDocking()
        {
            _currentState = DockingState.Aborted;
            RunStateMachine();
        }
        
        public void Update()
        {
            ProcessIGCMessages();
            RunStateMachine();
        }
        
        private void ProcessIGCMessages()
        {
            // Process unicast messages (responses)
            while (_unicastListener.HasPendingMessage)
            {
                MyIGCMessage message = _unicastListener.AcceptMessage();
                _program.OutText += $"Received unicast: {message.Tag}\n";
                
                if (message.Tag == PONG_TAG && message.Data is MyTuple<string, long, Vector3D>)
                {
                    var responsePacket = (MyTuple<string, long, Vector3D>)message.Data;
                    string gridName = responsePacket.Item1;
                    long gridAddress = responsePacket.Item2;
                    Vector3D gridPosition = responsePacket.Item3;
                    
                    _discoveredGridsCustomName[gridAddress] = gridName;
                    _discoveredGridsPosition[gridAddress] = gridPosition;
                    
                    _program.OutText += $"Discovered controller: {gridName} at {gridPosition}\n";
                }
                else if (message.Tag == DOCKREQUEST_TAG && message.Data is MyTuple<bool, Vector3D, Vector3D, Vector3D>)
                {
                    var responsePacket = (MyTuple<bool, Vector3D, Vector3D, Vector3D>)message.Data;
                    bool success = responsePacket.Item1;
                    
                    if (success)
                    {
                        _currentState = DockingState.Docking;
                        _targetConnectorPosition = responsePacket.Item2;
                        _targetConnectorUp = responsePacket.Item3;
                        _targetConnectorForward = responsePacket.Item4;
                        _program.OutText += "Docking connector assigned, beginning approach\n";
                    }
                    else
                    {
                        _currentState = DockingState.Error;
                        _program.OutText += "Target has no available connectors\n";
                    }
                }
            }
        }
        
        private void RunStateMachine()
        {
            switch (_currentState)
            {
                case DockingState.Setup:
                    _program.Echo("DockingState: Setting up docking systems...");
                    FindShipComponents();
                    _currentState = DockingState.Ping;
                    break;
                    
                case DockingState.Ping:
                    _program.Echo("DockingState: Sending discovery ping...");
                    _program.IGC.SendBroadcastMessage(PING_CHANNEL, "PING");
                    _currentState = DockingState.WaitingForResponse;
                    break;
                    
                case DockingState.WaitingForResponse:
                    _program.Echo($"DockingState: Waiting for responses... (found {_discoveredGridsPosition.Count} stations)");
                    if (_discoveredGridsPosition.Count > 0)
                    {
                        _targetGrid = FindClosestDockingTarget();
                        _program.Echo($"Selected target grid: {_targetGrid}");
                        _currentState = DockingState.RequestingConnector;
                    }
                    break;
                    
                case DockingState.RequestingConnector:
                    if (_targetGrid != 0)
                    {
                        _program.Echo($"DockingState: Requesting docking with {_discoveredGridsCustomName[_targetGrid]}");
                        var requestPacket = _controller.GetPosition();
                        _program.IGC.SendUnicastMessage(_targetGrid, DOCKREQUEST_TAG, requestPacket);
                        _currentState = DockingState.WaitingForAssignment;
                        _shipConnector.Enabled = true;
                    }
                    break;
                    
                case DockingState.WaitingForAssignment:
                    _program.Echo("DockingState: Waiting for connector assignment...");
                    break;
                    
                case DockingState.Docking:
                    _program.Echo("DockingState: Performing docking maneuvers...");
                    PerformDocking();
                    break;
                    
                case DockingState.Complete:
                    _program.Echo("DockingState: Docking complete!");
                    CleanupDocking(true);
                    break;
                    
                case DockingState.Aborted:
                    _program.Echo("DockingState: Docking aborted.");
                    CleanupDocking(false);
                    break;
                    
                case DockingState.Error:
                    _program.Echo("DockingState: Docking error - no available connectors");
                    CleanupDocking(false);
                    break;
            }
        }
        
        private long FindClosestDockingTarget()
        {
            if (_controller == null) return 0;
            
            long gridId = 0;
            double closestDistance = double.MaxValue;
            
            MatrixD worldMatrix = _controller.WorldMatrix;
            Vector3D controllerPos = _controller.GetPosition();
            
            foreach (var kvp in _discoveredGridsPosition)
            {
                // Use line tracing to find the grid most aligned with our forward direction
                Vector3D closestPoint = GetClosestPointOnLine(controllerPos, worldMatrix.Forward, kvp.Value);
                double distance = Vector3D.Distance(closestPoint, kvp.Value);
                
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    gridId = kvp.Key;
                }
            }
            
            return gridId;
        }
        
        private Vector3D GetClosestPointOnLine(Vector3D linePoint, Vector3D lineDirection, Vector3D point)
        {
            Vector3D pointToLinePoint = point - linePoint;
            double t = Vector3D.Dot(pointToLinePoint, lineDirection) / lineDirection.LengthSquared();
            return linePoint + lineDirection * t;
        }
        
        private void PerformDocking()
        {
            if (_controller == null || _shipConnector == null) return;
            
            // Use the existing gyro control system from the drone
            bool aligned = AlignToTarget(_targetConnectorUp, -_targetConnectorForward);
            
            if (aligned)
            {
                Vector3D connectorPos = _shipConnector.GetPosition();
                Vector3D closestPointOnLine = GetClosestPointOnLine(_targetConnectorPosition, _targetConnectorForward, connectorPos);
                double distToLine = Vector3D.Distance(closestPointOnLine, connectorPos);
                
                if (distToLine > 1.0)
                {
                    _program.Echo($"Aligning to docking line: {distToLine:F1}m");
                    ThrustTowards(closestPointOnLine);
                }
                else
                {
                    double finalDist = Vector3D.Distance(connectorPos, _targetConnectorPosition);
                    _program.Echo($"Final approach: {finalDist:F1}m");
                    ThrustTowards(_targetConnectorPosition);
                    
                    if (finalDist < 3.0)
                    {
                        if (_shipConnector.Status == MyShipConnectorStatus.Connectable)
                        {
                            _shipConnector.Connect();
                        }
                        else if (_shipConnector.Status == MyShipConnectorStatus.Connected)
                        {
                            _currentState = DockingState.Complete;
                        }
                    }
                }
            }
            else
            {
                _program.Echo("Aligning to target orientation...");
            }
        }
        
        private bool AlignToTarget(Vector3D targetUp, Vector3D targetForward)
        {
            if (_shipConnector == null) return false;
            
            MatrixD connectorMatrix = _shipConnector.WorldMatrix;
            Vector3D currentUp = connectorMatrix.Up;
            Vector3D currentForward = connectorMatrix.Forward;
            
            // Calculate rotation error
            Vector3D rotationVec = Vector3D.Cross(currentForward, targetForward);
            rotationVec += Vector3D.Cross(currentUp, targetUp);
            
            // Use the existing gyro control system
            if (rotationVec.Length() < 0.01)
            {
                return true;
            }
            else
            {
                // Transform to local coordinates for gyro control
                Vector3D aimDirection = Vector3D.Normalize(targetForward);
                Vector3D upDirection = Vector3D.Normalize(targetUp);
                
                // Use the drone's existing gyro system
                _program._gyros?.FaceVectors(aimDirection, upDirection);
                return false;
            }
        }
        
        private void ThrustTowards(Vector3D targetPosition)
        {
            if (_controller == null || _shipConnector == null) return;
            
            // Disable dampeners to allow manual control
            _controller.DampenersOverride = false;
            
            Vector3D connectorPos = _shipConnector.GetPosition();
            double distance = Vector3D.Distance(targetPosition, connectorPos);
            
            // Scale thrust based on distance
            double thrustMultiplier = ClampedMap(distance, 30, 1, 0.2, 1.0);
            
            // Calculate desired movement direction
            Vector3D directionToTarget = Vector3D.Normalize(targetPosition - connectorPos) * _thrustersP 
                                       - _controller.GetShipVelocities().LinearVelocity * thrustMultiplier;
            
            // Use the existing thrust control system from the drone
            Vector3D stopPosition = _program.CalcStopPosition(-_controller.GetShipVelocities().LinearVelocity, connectorPos);
            Vector3D thrustVector = stopPosition - targetPosition;
            
            // Apply thrust using the drone's existing thrust control
            _program.ThrustControl(thrustVector, _program._upThrust, _program._downThrust, 
                                 _program._leftThrust, _program._rightThrust, 
                                 _program._forwardThrust, _program._backThrust);
        }
        
        private void CleanupDocking(bool successful)
        {
            if (_controller != null)
            {
                _controller.DampenersOverride = true;
            }
            
            // Reset gyros using existing system
            _program._gyros?.Reset();
            
            // Turn off all thrusters using existing system
            _program.SetThrust(-1f, _program._allThrust, false);
            
            _currentState = DockingState.Inactive;
            
            if (successful && _shipConnector != null)
            {
                _program.Echo("Successfully docked!");
            }
        }
        
        private static double ClampedMap(double value, double fromSource, double toSource, double fromTarget, double toTarget)
        {
            double mappedValue = fromTarget + (value - fromSource) * (toTarget - fromTarget) / (toSource - fromSource);
            return Math.Max(fromTarget, Math.Min(mappedValue, toTarget));
        }
        
        public string GetStatusText()
        {
            switch (_currentState)
            {
                case DockingState.Inactive:
                    return "Docking: Inactive";
                case DockingState.Setup:
                    return "Docking: Setting up...";
                case DockingState.Ping:
                    return "Docking: Scanning for stations...";
                case DockingState.WaitingForResponse:
                    return $"Docking: Found {_discoveredGridsPosition.Count} stations";
                case DockingState.RequestingConnector:
                    return "Docking: Requesting connector...";
                case DockingState.WaitingForAssignment:
                    return "Docking: Waiting for assignment...";
                case DockingState.Docking:
                    return "Docking: Approaching target...";
                case DockingState.Complete:
                    return "Docking: Complete";
                case DockingState.Aborted:
                    return "Docking: Aborted";
                case DockingState.Error:
                    return "Docking: Error - No connectors available";
                default:
                    return "Docking: Unknown state";
            }
        }
    }
}