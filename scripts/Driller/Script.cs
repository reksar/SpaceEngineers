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

    const int PIDegrees = 180;
    const float RadiansInCircle = 2 * (float)Math.PI;
    const int RotationCaliberDegrees = 5;
    const float RotationVelocity = 0.20F; // Rad/s

    enum State
    {
        RotateBackward = -1,
        Idle = 0,
        RotateForward = 1
    };
    State state;

    delegate void StateProcess(string argument, UpdateType updateSource);
    StateProcess currentStateProcess;

    IMyMotorAdvancedStator rotorR;
    IMyMotorAdvancedStator rotorL;

    public Program()
    {
        rotorR = GetBlock<IMyMotorAdvancedStator>("DrillRotorR");
        rotorL = GetBlock<IMyMotorAdvancedStator>("DrillRotorL");
        ChangeState_Idle();
    }

    public void Main(string argument, UpdateType invoker)
    {
        currentStateProcess(argument, invoker);
    }

    // State

    void StateIdle(string argument, UpdateType invoker)
    {
        if (IsInvokedByUser(invoker))
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

    void StateRotateForward(string argument, UpdateType updateSource)
    {
        if (rotorR.Angle >= rotorR.UpperLimitRad)
        {
            ChangeState_Idle();
        }
    }

    void StateRotateBackward(string argument, UpdateType updateSource)
    {
        if (rotorR.Angle <= rotorR.LowerLimitRad)
        {
            ChangeState_Idle();
        }
    }

    // SetState

    void SetStateIdle()
    {
        state = State.Idle;
        currentStateProcess = StateIdle;
    }

    void SetStateRotateForward()
    {
        state = State.RotateForward;
        currentStateProcess = StateRotateForward;
    }

    void SetStateRotateBackward()
    {
        state = State.RotateBackward;
        currentStateProcess = StateRotateBackward;
    }

    // ChangeState

    void ChangeState_Idle_RotateForward()
    {
        SetStateRotateForward();
        EnterStateRotate();
    }

    void ChangeState_Idle_RotateBackward()
    {
        SetStateRotateBackward();
        EnterStateRotate();
    }

    void ChangeState_Idle()
    {
        StopRotors();
        Runtime.UpdateFrequency = UpdateFrequency.None;
        SetStateIdle();
    }

    // EnterState

    void EnterStateRotate()
    {
        RotateRotors();
        Runtime.UpdateFrequency = UpdateFrequency.Update10;
    }

    // Other

    void StopRotors()
    {
        rotorR.RotorLock = true;
        rotorL.RotorLock = true;
        rotorR.TargetVelocityRad = 0;
        rotorL.TargetVelocityRad = 0;
    }

    void RotateRotors()
    {
        SetUpRotorR();
        rotorR.RotorLock = false;
    }

    void SetUpRotorR()
    {
        float rotationAngle = CalcRotationAngleR();
        SetLimitsRotorR(rotationAngle);
        SetSpeedRotorR();
    }

    float CalcRotationAngleR()
    {
        int rotationAngleDeg = RotationCaliberDegrees;
        int aberranceDeg = CalcAberranceDeg();
        if (aberranceDeg != 0)
        {
            if (state == State.RotateForward)
            {
                rotationAngleDeg = RotationCaliberDegrees - aberranceDeg;
            }
            else
            {
                rotationAngleDeg = aberranceDeg;
            }
        }
        return ToRadians(rotationAngleDeg);
    }

    int CalcAberranceDeg()
    {
        return ToDegrees(rotorR.Angle) % RotationCaliberDegrees;
    }

    void SetLimitsRotorR(float rotationAngle)
    {
        float newAngle = Math.Abs(rotorR.Angle + (int)state * rotationAngle);

        if (newAngle > RadiansInCircle)
        {
            newAngle -= RadiansInCircle;
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

    void SetSpeedRotorR()
    {
        rotorR.TargetVelocityRad = (int)state * RotationVelocity;
    }

    void SetRotorLimits(IMyMotorAdvancedStator rotor, float minAngle, float maxAngle)
    {
        rotor.LowerLimitRad = minAngle;
        rotor.UpperLimitRad = maxAngle;
    }

    T GetBlock<T>(string name)
    {
        return (T)GridTerminalSystem.GetBlockWithName(name);
    }

    bool IsInvokedByUser(UpdateType invoker)
    {
        return (invoker == UpdateType.Trigger)
            || (invoker == UpdateType.Terminal);
    }

    int ToDegrees(float radians)
    {
        const float DegreesInRadian = (float)Math.PI / PIDegrees;
        float degrees = DegreesInRadian * radians;
        return (int)Math.Round(degrees);
    }

    float ToRadians(int degrees)
    {
        return (degrees * (float)Math.PI) / PIDegrees;
    }

    // INGAME SCRIPT END
}