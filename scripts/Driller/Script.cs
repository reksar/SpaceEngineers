using System;

// Space Engineers game DLLs
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using Sandbox.Game.EntityComponents;
using VRageMath;
using VRage.Game;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;

public sealed class Program : MyGridProgram
{
    // INGAME SCRIPT START

    public enum State
    {
        ROTATE_BACKWARD = -1,
        IDLE = 0,
        ROTATE_FORWARD = 1
    };
    private State state;

    delegate void StateProcess(string argument, UpdateType updateSource);
    private StateProcess currentStateProcess;

    private IMyMotorAdvancedStator rotorR;
    private IMyMotorAdvancedStator rotorL;

    public const int PI_DEGREES = 180;
    public const float RADIANS_IN_CIRCLE = 2 * (float)Math.PI;
    public const int ROTATION_CALIBER_DEG = 5;
    public const float ROTATION_VELOCITY = 0.20F; // Rad/s

    public Program()
    {
        rotorR = GridTerminalSystem.GetBlockWithName("DrillRotorR") as IMyMotorAdvancedStator;
        rotorL = GridTerminalSystem.GetBlockWithName("DrillRotorL") as IMyMotorAdvancedStator;
        ChangeState_Idle();
    }

    public void Save()
    {

    }

    public void Main(string argument, UpdateType updateSource)
    {
        currentStateProcess(argument, updateSource);
    }

    // State

    private void StateIdle(string argument, UpdateType updateSource)
    {
        if ((updateSource == UpdateType.Trigger) || (updateSource == UpdateType.Terminal))
        {
            switch (argument)
            {
                case "RotateForward":
                    ChangeState_Idle_RotateForward();
                    break;

                case "RotateBackward":
                    ChangeState_Idle_RotateBackward();
                    break;

                default:
                    break;
            }
        }
    }

    private void StateRotateForward(string argument, UpdateType updateSource)
    {
        if (rotorR.Angle >= rotorR.UpperLimitRad)
        {
            ChangeState_Idle();
        }
    }

    private void StateRotateBackward(string argument, UpdateType updateSource)
    {
        if (rotorR.Angle <= rotorR.LowerLimitRad)
        {
            ChangeState_Idle();
        }
    }

    // SetState

    private void SetStateIdle()
    {
        state = State.IDLE;
        currentStateProcess = StateIdle;
    }

    private void SetStateRotateForward()
    {
        state = State.ROTATE_FORWARD;
        currentStateProcess = StateRotateForward;
    }

    private void SetStateRotateBackward()
    {
        state = State.ROTATE_BACKWARD;
        currentStateProcess = StateRotateBackward;
    }

    // ChangeState

    private void ChangeState_Idle_RotateForward()
    {
        SetStateRotateForward();
        EnterStateRotate();
    }

    private void ChangeState_Idle_RotateBackward()
    {
        SetStateRotateBackward();
        EnterStateRotate();
    }

    private void ChangeState_Idle()
    {
        StopRotors();
        Runtime.UpdateFrequency = UpdateFrequency.None;
        SetStateIdle();
    }

    // EnterState

    private void EnterStateRotate()
    {
        RotateRotors();
        Runtime.UpdateFrequency = UpdateFrequency.Update10;
    }

    // Other

    private void StopRotors()
    {
        rotorR.RotorLock = true;
        rotorL.RotorLock = true;
        rotorR.TargetVelocityRad = 0;
        rotorL.TargetVelocityRad = 0;
    }

    private void RotateRotors()
    {
        SetUpRotorR();
        rotorR.RotorLock = false;
    }

    private void SetUpRotorR()
    {
        float rotationAngle = CalcRotationAngleR();
        SetLimitsRotorR(rotationAngle);
        SetSpeedRotorR();
    }

    private float CalcRotationAngleR()
    {
        int rotationAngleDeg = ROTATION_CALIBER_DEG;
        int aberranceDeg = CalcAberranceDeg();
        if (aberranceDeg != 0)
        {
            if (state == State.ROTATE_FORWARD)
            {
                rotationAngleDeg = ROTATION_CALIBER_DEG - aberranceDeg;
            }
            else
            {
                rotationAngleDeg = aberranceDeg;
            }
        }
        return ToRadians(rotationAngleDeg);
    }

    private int CalcAberranceDeg()
    {
        return ToDegrees(rotorR.Angle) % ROTATION_CALIBER_DEG;
    }

    private void SetLimitsRotorR(float rotationAngle)
    {
        float newAngle = Math.Abs(rotorR.Angle + (int)state * rotationAngle);

        if (newAngle > RADIANS_IN_CIRCLE)
        {
            newAngle -= RADIANS_IN_CIRCLE;
        }

        if (newAngle > rotorR.Angle)
        {
            SetRotorLimits(rotorR, rotorR.Angle, newAngle);
        }
        else
        {
            SetRotorLimits(rotorR, newAngle, rotorR.Angle);
        }
    }

    private void SetSpeedRotorR()
    {
        rotorR.TargetVelocityRad = (int)state * ROTATION_VELOCITY;
    }

    private void SetRotorLimits(IMyMotorAdvancedStator rotor, float minAngle, float maxAngle)
    {
        rotor.LowerLimitRad = minAngle;
        rotor.UpperLimitRad = maxAngle;
    }

    public int ToDegrees(float radians)
    {
        const float DEGREES_IN_RADIAN = (float)Math.PI / PI_DEGREES;
        float degrees = DEGREES_IN_RADIAN * radians;
        return (int)Math.Round(degrees);
    }

    public float ToRadians(int degrees)
    {
        return (degrees * (float)Math.PI) / PI_DEGREES;
    }

    // INGAME SCRIPT END
}