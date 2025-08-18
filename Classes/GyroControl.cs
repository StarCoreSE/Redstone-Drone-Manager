using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using VRageMath;

/// <summary>
/// Whip's GyroControl class - handles 3-axis rotation using PID controllers
/// </summary>
public class GyroControl
{
    private List<IMyGyro> _gyros;
    private IMyTerminalBlock _reference;
    private Vector3PID _pid;
    private long _gridId; 

    public GyroControl(MyGridProgram program, IMyTerminalBlock reference, double kP, double kI, double kD, double lowerBound, double upperBound, double timeStep)
    {
        _reference = reference;
        _gridId = reference.CubeGrid.EntityId; // Store the grid ID
        _gyros = new List<IMyGyro>();
        
        // Filter gyros to only those on our grid
        program.GridTerminalSystem.GetBlocksOfType(_gyros, g => g.CubeGrid.EntityId == _gridId);
        
        _pid = new Vector3PID(kP, kI, kD, lowerBound, upperBound, timeStep);
        Reset();
    }

    public GyroControl(MyGridProgram program, IMyTerminalBlock reference, List<IMyGyro> gyros, double kP, double kI, double kD, double lowerBound, double upperBound, double timeStep)
    {
        _reference = reference;
        _gridId = reference.CubeGrid.EntityId;
        
        // Filter the provided gyros list
        _gyros = gyros.Where(g => g.CubeGrid.EntityId == _gridId).ToList();
        
        _pid = new Vector3PID(kP, kI, kD, lowerBound, upperBound, timeStep);
        Reset();
    }

    public void Reset()
    {
        for (int i = 0; i < _gyros.Count; i++)
        {
            var gyro = _gyros[i];
            if (gyro == null)
            {
                _gyros.RemoveAtFast(i);
                continue;
            }
            gyro.GyroOverride = false;
        }
        _pid.Reset();
    }

    private Vector3D GetRotationAngles(MatrixD worldMatrix, Vector3D desiredForwardVector, Vector3D desiredUpVector)
    {
        var angles = new Vector3D();

        if (desiredForwardVector != Vector3D.Zero)
        {
            var quaternion = Quaternion.CreateFromForwardUp(worldMatrix.Forward, worldMatrix.Up);
            var inverseQuaternion = Quaternion.Inverse(quaternion);
            var localVector = Vector3D.Transform(desiredForwardVector, inverseQuaternion);

            Vector3D.GetAzimuthAndElevation(localVector, out angles.Y, out angles.X);
        }

        if (desiredUpVector != Vector3D.Zero)
        {
            var rejectedUpVector = VectorRejection(desiredUpVector, _reference.WorldMatrix.Forward);
            var normalizedRejectedUp = Vector3D.Normalize(rejectedUpVector);

            double dotProduct = MathHelper.Clamp(Vector3D.Dot(_reference.WorldMatrix.Up, normalizedRejectedUp), -1, 1);
            double rollAngle = Math.Acos(dotProduct);
            double rollSign = Math.Sign(VectorDot(normalizedRejectedUp, _reference.WorldMatrix.Right));
            
            if (rollSign > 0)
                rollAngle *= -1;

            angles.Z = rollAngle;
        }

        if (Math.Abs(angles.X) < 0.001) angles.X = 0;
        if (Math.Abs(angles.Y) < 0.001) angles.Y = 0;
        if (Math.Abs(angles.Z) < 0.001) angles.Z = 0;

        return angles;
    }

    public void FaceVectors(Vector3D desiredForwardVector, Vector3D desiredUpVector)
    {
        var angles = -GetRotationAngles(_reference.WorldMatrix, desiredForwardVector, desiredUpVector);
        var pidOutput = new Vector3D(_pid.Control(angles));
        ApplyGyroOverride(_reference.WorldMatrix, pidOutput);
    }

    private void ApplyGyroOverride(MatrixD worldMatrix, Vector3D rotationVector)
    {
        var transformedRotationVec = Vector3D.TransformNormal(rotationVector, worldMatrix);

        foreach (var gyro in _gyros)
        {
            var gyroRotationVec = Vector3D.TransformNormal(transformedRotationVec, MatrixD.Transpose(gyro.WorldMatrix));

            if (!gyroRotationVec.IsValid())
                throw new Exception("Invalid gyro rotation vector: " + gyroRotationVec.ToString());

            gyro.Pitch = (float)gyroRotationVec.X;
            gyro.Yaw = (float)gyroRotationVec.Y;
            gyro.Roll = (float)gyroRotationVec.Z;
            gyro.GyroOverride = true;
        }
    }

    public static double VectorDot(Vector3D a, Vector3D b)
    {
        double dot = Vector3D.Dot(a, b);
        if (double.IsNaN(dot))
            return 0;
        return dot;
    }

    public static Vector3D VectorProjection(Vector3D a, Vector3D b)
    {
        return VectorDot(a, b) * b;
    }

    public static Vector3D VectorRejection(Vector3D a, Vector3D b)
    {
        return a - VectorProjection(a, b);
    }
}

/// <summary>
/// 3-axis PID controller using individual PID controllers for each axis
/// </summary>
public class Vector3PID
{
    private ClampedIntegralPID _xPid;
    private ClampedIntegralPID _yPid;
    private ClampedIntegralPID _zPid;

    public Vector3PID(double kP, double kI, double kD, double lowerBound, double upperBound, double timeStep)
    {
        _xPid = new ClampedIntegralPID(kP, kI, kD, timeStep, lowerBound, upperBound);
        _yPid = new ClampedIntegralPID(kP, kI, kD, timeStep, lowerBound, upperBound);
        _zPid = new ClampedIntegralPID(kP, kI, kD, timeStep, lowerBound, upperBound);
    }

    public Vector3PID(double kP, double kI, double kD, double decayRatio, double timeStep)
    {
        _xPid = new ClampedIntegralPID(kP, kI, kD, timeStep, -1000, 1000);
        _yPid = new ClampedIntegralPID(kP, kI, kD, timeStep, -1000, 1000);
        _zPid = new ClampedIntegralPID(kP, kI, kD, timeStep, -1000, 1000);
    }

    public Vector3D Control(Vector3D error)
    {
        return new Vector3D(_xPid.Control(error.X), _yPid.Control(error.Y), _zPid.Control(error.Z));
    }

    public void Reset()
    {
        _xPid.Reset();
        _yPid.Reset();
        _zPid.Reset();
    }
}