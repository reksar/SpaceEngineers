using System;

// Space Engineers game DLLs
using VRageMath;
using VRage.Game;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using Sandbox.ModAPI.Ingame;
using Sandbox.Game.EntityComponents;
using SpaceEngineers.Game.ModAPI.Ingame;

public sealed class Program : MyGridProgram
{
    // INGAME SCRIPT START

    public const char COMMA = ',';
    IMyTextPanel panel;
    int pCount = 0;
    int mCount = 0;

    public Program()
    {
        panel = GridTerminalSystem.GetBlockWithName("DrillStatusLCD") as IMyTextPanel;
        if (Storage == "")
        {
            Storage = "0" + COMMA + "0";
        }
        pCount++;
        Increase(0);
        panel.WriteText(Storage + "   " + pCount.ToString() + "   " + mCount.ToString());
    }

    public void Save()
    {

    }

    public void Main(string argument, UpdateType updateSource)
    {
        mCount++;
        Increase(1);
        panel.WriteText(Storage + "   " + pCount.ToString() + "   " + mCount.ToString());
    }

    private void Increase(int idx)
    {
        string[] parts = Storage.Split(COMMA);
        string count = parts[idx];
        int num = 0;
        Int32.TryParse(count, out num);
        num++;
        count = num.ToString();
        parts[idx] = count;
        Storage = parts[0] + COMMA + parts[1];
    }

    // INGAME SCRIPT END
}