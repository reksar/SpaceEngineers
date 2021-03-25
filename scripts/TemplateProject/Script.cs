/*
 * For the Space Engineers (https://www.spaceengineersgame.com) game.
 * It is the template for writing an ingame scripts, that are can be executed 
 * by Programming Block inside the game.
 * 
 * A code between the // INGAME SCRIPT START and // INGAME SCRIPT END markers
 * is the ingame script. It can be placed either into the Programming Block 
 * inside the game or into the file:
 * ...\AppData\Roaming\SpaceEngineers\IngameScripts\local\<ScriptName>\Script.cs
 * Also, these markers are required for script exporter (see `..\export.bat`).
 *
 * The rest code wraps the game script for development purposes, e.g. code 
 * autocompletion.
 */

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