# Environment Setup

Complete guide for setting up the development environment for Verdure Assistant.

## System Requirements

- **.NET 9.0 SDK** or higher
- **Visual Studio 2022** 17.8+ or **Visual Studio Code**
- **Git** for version control
- **Windows 10/11** (for WinUI development)

## Installation Steps

### 1. Install .NET 9 SDK

Download and install from [.NET download page](https://dotnet.microsoft.com/download/dotnet/9.0)

Verify installation:
```bash
dotnet --version
# Should display 9.0.x or higher
```

### 2. Clone the Repository

```bash
git clone https://github.com/maker-community/Verdure.Assistant.git
cd Verdure.Assistant
```

### 3. Restore Dependencies

```bash
dotnet restore
```

### 4. Build the Project

```bash
dotnet build --configuration Release
```

## Development Tools

### Visual Studio 2022

Required workloads:
- .NET desktop development
- ASP.NET and web development
- .NET Multi-platform App UI development

### Visual Studio Code

Required extensions:
- C# Dev Kit
- PowerShell
- GitLens

## Verification

Create and run a test project:

```bash
mkdir test-project && cd test-project
dotnet new console
dotnet run
```

If you see "Hello, World!" output, your environment is ready!

## Next Steps

- [Getting Started](/en/guide/getting-started) - Run your first project
- [Project Architecture](/en/guide/architecture) - Understand the system design
- [Tech Stack](/en/guide/tech-stack) - Learn about the technologies used