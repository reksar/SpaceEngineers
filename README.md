# Space Engineers scripts

Toolkit for scripting a *Programmable Block* in the 
[Space Engineers](https://www.spaceengineersgame.com) game.

**Features**

* [IntelliSense](https://code.visualstudio.com/docs/editor/intellisense) for
the game API (DLLs are plugged in `SpaceEngineers.csproj`)

* Automatic export to game local storage when saving a `Script.cs`

# Requirements

* [Git for Windows](https://git-scm.com/download/win)

* [.NET SDK](https://dotnet.microsoft.com)
(notice `<TargetFramework>net6.0</TargetFramework>` in `SpaceEngineers.csproj`)

* [VS Code](https://code.visualstudio.com) with
[C#](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csharp) 
extension (build and debug assets are not required)

# Usage

## Create a new script

Run the `Create Space Engineers script` task from the
`Terminal` -> `Run Task...` menu, then enter a name for the new script.
This name will be used as the dir name and C# namespace/region identifier.

You can copy `scripts\Template` -> `scripts\[NewName]` manually and replace all
`Template` entries with `[NewName]` in `Script.cs` file.

## Save and Export

Add the following key binding to
`%userprofile%\AppData\Roaming\Code\User\keybindings.json`:

``` JSON
{
  "key": "ctrl+s",
  "command": "workbench.action.tasks.runTask",
  "args": "Export Space Engineers script",
  "when": "resourceFilename == Script.cs && editorTextFocus"
}
```

When you save the `Script.cs` by pressing `Ctrl+S`, its dir will be copied to
the game local storage.

## Git filters (optional)

To prevent committing your local settings, run `utils\git\config.bat`.
