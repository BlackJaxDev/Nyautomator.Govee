# Nyautomator Govee Module

Govee smart-device integration module for Nyautomator. It owns the Govee Open API client, realtime device event stream, runtime participant, automation nodes, device tab, dashboard and settings cards, MCP tools, graph templates, and packaging.

## Source Layout

- `src/Nyautomator.Govee`: Govee Open API client, realtime event stream, and models.
- `src/Nyautomator.Runtime.Integrations.Govee`: Govee runtime service with device cache and event forwarding.
- `src/Nyautomator.Automation.Govee`: Govee automation events, reactions, value nodes, and option providers.
- `src/Nyautomator.Module.Govee`: Module entrypoint, runtime participant, and `/modules/govee/api/*` handler.
- `tests/Nyautomator.Module.Govee.Tests`: API handler and automation node tests.
- `module/module.json`: Nyautomator module contribution manifest.
- `module/assets`: icon, settings/dashboard/tab UI, config schema, and graph template assets.

## Build

From inside a Nyautomator checkout (`Submodules/Nyautomator.Govee`), the projects reference the host SDK projects by source:

```powershell
dotnet build .\Nyautomator.Govee.slnx
dotnet test .\Nyautomator.Govee.slnx
```

Standalone binary-reference build against host SDK DLLs:

```powershell
dotnet build .\Nyautomator.Govee.slnx -p:UseNyautomatorProjectReferences=false -p:NyautomatorReferencePath=C:\Path\To\NyautomatorDlls\
```

## Packaging

```powershell
.\scripts\pack-module.ps1 -Configuration Release -UseNyautomatorProjectReferences false -NyautomatorReferencePath C:\Path\To\NyautomatorDlls\
```

The script writes `artifacts/modules/govee-<version>.zip`. Install it from the Nyautomator **Modules** tab. The package contains only Govee-owned assemblies, required third-party dependencies, `module.json`, and `module/assets/**`; host SDK assemblies stay compile-time references and are excluded.
