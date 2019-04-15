/*
 * Шаблон для написания внутриигровых скриптов
 * для программируемого блока в игре Space Engineers.
 * 
 * Непосредственно внутриигровой скрипт находится между строками
 * "INGAME SCRIPT START" и "INGAME SCRIPT END".
 * Этот скрипт можно скопировать напрямую в тектовое поле
 * программируемого блока в игре, или экспортировать в файл
 * `<...>\AppData\Roaming\SpaceEngineers\IngameScripts\local\<ScriptName>\Script.cs`
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

    public Program()
    {
        // Конструктор, вызванный единожды в каждой сессии и
        // всегда перед вызовом других методов. Используйте его,
        // чтобы инициализировать ваш скрипт.
        //  
        // Конструктор опционален и может быть удалён, 
        // если в нём нет необходимости.
        // 
        // Рекомендуется использовать его, чтобы установить RuntimeInfo.UpdateFrequency,
        // что позволит перезапускать ваш скрипт автоматически, без нужды в таймере.
    }

    public void Save()
    {
        // Вызывается, когда программе требуется сохранить своё состояние.
        // Используйте этот метод, чтобы сохранить состояние программы
        // в строковое поле Storage или в другое место.
        // 
        // Этот метод опционален и может быть удалён, если не требуется.
    }

    public void Main(string argument, UpdateType updateSource)
    {
        // Главная точка входа в скрипт вызывается каждый раз,
        // когда действие Запуск программного блока активируется,
        // или скрипт самозапускается. Аргумент updateSource описывает,
        // откуда поступило обновление.
        // 
        // Метод необходим сам по себе, но аргументы
        // могут быть удалены, если не требуются. 
    }

    // INGAME SCRIPT END
}