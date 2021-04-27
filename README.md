# Space Engineers scripts

Toolkit for scripting a *Programmable Block* in the 
[Space Engineers](https://www.spaceengineersgame.com) game.

**Features**
* [IntelliSense](https://code.visualstudio.com/docs/editor/intellisense) for
the game API (DLLs are plugged in `SpaceEngineers.csproj`)
* Export to the game local storage during saving a `Script.cs` by pressing 
`Ctrl+S`

# Usage

## Create a new script

Run `Create Space Engineers script` task from `Terminal`->`Run Task...` menu.

Then enter a name for your new script. It must be a valid dir name and a C# 
namespace/region identifier.

You can copy `scripts\Template` -> `scripts\[NewName]` manually and replace
the `Template` word with `[NewName]` in `Script.cs`.

## Export to the game

Save a `Script.cs` by pressing `Ctrl+S`.

The dir containing the active `Script.cs` will be copied to the game.

# Setup

## Main stuff with IntelliSense support

* [Git for Windows](https://git-scm.com/download/win)
* [.NET SDK](https://dotnet.microsoft.com/)
* [VS Code](https://code.visualstudio.com/) editor with 
[C#](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csharp) 
extension

When C# extension of VS Code notificates that build and debug assets are 
missing, it is not required to add them.

## Configure utility scripts

* Set full path without spaces and without slash at the end for `GIT_DIR` in 
the `utils\config.bat`

* Add the following key binding into the 
`%userprofile%\AppData\Roaming\Code\User\keybindings.json`:

```json
{
    "key": "ctrl+s",
    "command": "workbench.action.tasks.runTask",
    "args": "Export Space Engineers script",
    "when": "resourceFilename == Script.cs && editorTextFocus"
}
```

I was inspired by 
[this comment](https://github.com/gregretkowski/VSC-SE/issues/1#issuecomment-812445939) 
and decided to make more universal and lightweight solution.

## Git filters (optional)

To prevent committing your local settings, run `utils\git\config.bat`.
