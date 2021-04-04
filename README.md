# Space Engineers scripts

The toolkit for scripting the *Programmable Block* in the [Space Engineers](https://www.spaceengineersgame.com) game.

**Features**
* [IntelliSense](https://code.visualstudio.com/docs/editor/intellisense)
* Export to the game during saving `Script.cs` by pressing
`Ctrl+S`

See `scripts\Template\` and `scripts\export.bat`

Game DLLs are plugged in `SpaceEngineers.csproj`

# Setup

## Main stuff with IntelliSense support
* [Git for Windows](https://git-scm.com/download/win)
* [.NET SDK](https://dotnet.microsoft.com/)
* [VS Code](https://code.visualstudio.com/) editor with [C#](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csharp) extension

When C# extension of VS Code notificates that build and debug assets are 
missing, it is not required to add them.

## Scripts export settings

* Set full path without spaces for `GIT_DIR` in the `scripts\export.bat` file.

* Add the following key binding:

```json
# %userprofile%\AppData\Roaming\Code\User\keybindings.json

{
    "key": "ctrl+s",
    "command": "workbench.action.tasks.runTask",
    "when": "resourceFilename == Script.cs && editorTextFocus",

    // Task label
    "args": "Export Space Engineers script"
}
```

I was inspired by [this comment](https://github.com/gregretkowski/VSC-SE/issues/1#issuecomment-812445939) 
and decided to make more universal and lightweight solution.
