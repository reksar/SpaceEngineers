/*
 * WARNING!
 * `// INGAME SCRIPT START` and `// INGAME SCRIPT END` are required markers!
 * Do not delete them, because the `export.bat` will not be able to work!
 *
 * A code between the markers is the ingame script. It can be placed either 
 * into the Programming Block inside the game directly or into the file:
 * `<...>\AppData\Roaming\SpaceEngineers\IngameScripts\local\<Name>\Script.cs`
 *
 * The rest code is the wrapper for development purposes.
 */

using System;

// Space Engineers DLLs
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

    /*
     * Constructor is optional. It will executed once per game session and 
     * before any other methods will be invoked.
     *
     * It is recommended to set `RuntimeInfo.UpdateFrequency` here for 
     * restarting the script automatically without timer.
     */
    public Program() {}

    /*
     * Optional method. Call it to save the script state into the `Storage` 
     * string field or some other place.
     */
    public void Save() {}

    /*
     * Runs every time either on Programming Block `Run`, or when script starts 
     * automatically. Methos is required, but arguments are not.
     */
    public void Main(string argument, UpdateType updateSource) {}

    // INGAME SCRIPT END
}