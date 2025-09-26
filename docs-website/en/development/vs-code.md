# VS Code Development Environment

Guide for developing Verdure Assistant using Visual Studio Code.

## Environment Setup

### Required Extensions

Install essential extensions for C# development:

```bash
# Install C# development extensions
code --install-extension ms-dotnettools.csdevkit
code --install-extension ms-vscode.powershell  
code --install-extension eamodio.gitlens
code --install-extension ms-vscode.vscode-json
```

## Project Configuration

### Opening the Project

```bash
# Clone and open the project
git clone https://github.com/maker-community/Verdure.Assistant.git
cd Verdure.Assistant
code .
```

### Debug Configuration

Create `.vscode/launch.json`:

```json
{
    "version": "0.2.0",
    "configurations": [
        {
            "name": "Console",
            "type": "coreclr",
            "request": "launch",
            "program": "${workspaceFolder}/src/Verdure.Assistant.Console/bin/Debug/net9.0/Verdure.Assistant.Console.dll",
            "args": [],
            "cwd": "${workspaceFolder}/src/Verdure.Assistant.Console",
            "console": "internalConsole",
            "stopAtEntry": false
        },
        {
            "name": "API",
            "type": "coreclr", 
            "request": "launch",
            "program": "${workspaceFolder}/src/Verdure.Assistant.Api/bin/Debug/net9.0/Verdure.Assistant.Api.dll",
            "args": [],
            "cwd": "${workspaceFolder}/src/Verdure.Assistant.Api",
            "env": {
                "ASPNETCORE_ENVIRONMENT": "Development"
            }
        }
    ]
}
```

### Task Configuration

Create `.vscode/tasks.json`:

```json
{
    "version": "2.0.0",
    "tasks": [
        {
            "label": "build",
            "command": "dotnet",
            "type": "process",
            "args": [
                "build",
                "${workspaceFolder}/Verdure.Assistant.sln",
                "/property:GenerateFullPaths=true",
                "/consoleloggerparameters:NoSummary"
            ],
            "problemMatcher": "$msCompile",
            "group": {
                "kind": "build",
                "isDefault": true
            }
        },
        {
            "label": "test",
            "command": "dotnet",
            "type": "process", 
            "args": [
                "test",
                "${workspaceFolder}/Verdure.Assistant.sln"
            ],
            "problemMatcher": "$msCompile",
            "group": "test"
        }
    ]
}
```

## Development Tips

### Keyboard Shortcuts

- **Ctrl+Shift+P** - Command Palette
- **F5** - Start Debugging
- **Ctrl+F5** - Run Without Debugging
- **Ctrl+Shift+`** - New Terminal
- **Ctrl+K Ctrl+S** - Keyboard Shortcuts

### IntelliSense and Code Navigation

- **Go to Definition**: F12
- **Peek Definition**: Alt+F12
- **Find All References**: Shift+F12
- **Rename Symbol**: F2

### Integrated Terminal

Use the integrated terminal for dotnet commands:

```bash
# Build the solution
dotnet build

# Run tests
dotnet test

# Run a specific project
dotnet run --project src/Verdure.Assistant.Console
```

## Extensions for Enhanced Development

### Recommended Extensions

```bash
# Code formatting and analysis
code --install-extension editorconfig.editorconfig
code --install-extension ms-vscode.hexeditor

# Git integration
code --install-extension github.vscode-pull-request-github
code --install-extension mhutchie.git-graph

# Documentation
code --install-extension yzhang.markdown-all-in-one
code --install-extension shd101wyy.markdown-preview-enhanced
```

### Workspace Settings

Create `.vscode/settings.json`:

```json
{
    "dotnet.defaultSolution": "Verdure.Assistant.sln",
    "files.exclude": {
        "**/bin": true,
        "**/obj": true,
        "**/.vs": true
    },
    "editor.formatOnSave": true,
    "editor.codeActionsOnSave": {
        "source.fixAll": "explicit"
    },
    "csharp.semanticHighlighting.enabled": true
}
```

## Debugging Features

### Breakpoint Management

- Click in the gutter to set breakpoints
- Right-click for conditional breakpoints
- Use logpoints for non-intrusive debugging

### Variable Inspection

- Hover over variables to see values
- Use the Variables panel in Debug view
- Add expressions to the Watch panel

### Debug Console

Use the Debug Console for runtime evaluation:

```csharp
// Evaluate expressions at runtime
myVariable.PropertyName
SomeMethod(parameter)
```

## Git Integration

### Source Control Panel

- View file changes in the Source Control panel
- Stage and commit changes directly in VS Code
- Compare file versions with built-in diff viewer

### GitLens Features

- View git blame information inline
- Explore repository history
- Compare branches and commits

## Related Resources

- **[Environment Setup](/en/guide/environment-setup)** - Initial setup requirements
- **[Debugging Guide](/en/development/debugging)** - Advanced debugging techniques
- **[Project Structure](/en/guide/architecture)** - Understanding the codebase