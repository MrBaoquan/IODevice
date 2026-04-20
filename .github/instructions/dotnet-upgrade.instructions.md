---
description: "Specialized instructions for .NET framework version upgrades"
applyTo: "**/*.csproj,**/*.sln"
---

# .NET Project Upgrade Instructions

This document provides structured guidance for upgrading a multi-project .NET solution to a higher framework version (e.g., .NET 6 → .NET 8). Follow the steps **sequentially** and **do not attempt to upgrade all projects at once**.

## Preparation

1. **Identify Project Type** — Inspect each `*.csproj`:
   - `netcoreapp*` → **.NET Core / .NET (modern)**
   - `netstandard*` → **.NET Standard**
   - `net4*` (e.g., net472) → **.NET Framework**
   - Note the current target and SDK.

2. **Select Target Version**
   - **.NET (Core/Modern)**: Upgrade to the latest LTS (e.g., `net10.0`).
   - **.NET Standard**: Prefer migrating to **.NET 8+** if possible.
   - **.NET Framework**: Upgrade to at least **4.8**, or migrate to .NET 8+ if feasible.

3. **Review Release Notes & Breaking Changes**
   - [.NET Upgrade Docs](https://learn.microsoft.com/dotnet/core/whats-new/)

## 1. Upgrade Strategy

1. Upgrade **projects sequentially**, not all at once.
2. Start with **independent class library projects** (least dependencies).
3. Gradually move to projects with **higher dependencies**.
4. Ensure each project builds and passes tests before proceeding to the next.

## 2. Determine Upgrade Sequence

To identify dependencies:

```bash
dotnet list <ProjectName>.csproj reference
```

## 3. Analyze Each Project

For each project:

1. Open the `*.csproj` file.
2. Check for:
   - `TargetFramework` → Change to the desired version (e.g., `net8.0`).
   - `PackageReference` → Verify if each NuGet package supports the new framework.
     ```bash
     dotnet list package --outdated
     dotnet add package <PackageName> --version <LatestVersion>
     ```

## 4. Upgrade Process Per Project

1. Update `TargetFramework` in `.csproj`.
2. Update NuGet packages to versions compatible with the target framework.
3. After upgrading, review code for any required changes.
4. Rebuild the project:
   ```bash
   dotnet build <ProjectName>.csproj
   ```
5. Run unit tests if any:
   ```bash
   dotnet test
   ```
6. Fix build or runtime issues before proceeding.

## 5. Handling Breaking Changes

- Review [.NET Upgrade Assistant](https://learn.microsoft.com/dotnet/core/porting/upgrade-assistant) suggestions.
- Common issues:
  - Deprecated APIs → Replace with supported alternatives.
  - Package incompatibility → Find updated NuGet or migrate to Microsoft-supported library.
  - Configuration differences (e.g., `Startup.cs` → `Program.cs` in .NET 8+).

## 6. Validate End-to-End

After all projects are upgraded:

1. Rebuild entire solution.
2. Run all automated tests (unit, integration).
3. Validate APIs start without runtime errors.

## 7. Tools & Automation

```bash
dotnet tool install -g upgrade-assistant
upgrade-assistant upgrade <SolutionName>.sln
```

## 8. Commit Plan

- Commit after each successful project upgrade.
- If a project fails, rollback to the previous commit and fix incrementally.

## 9. Upgrade Checklist (Per Project)

| Project Name | Target Framework | Dependencies Updated | Builds Successfully | Tests Passing | Notes |
| ------------ | ---------------- | -------------------- | ------------------- | ------------: | ----- |
| IOStudio     | ☐ net8.0         | ☐                    | ☐                   |             ☐ |       |
| DNHper       | ☐ net8.0         | ☐                    | ☐                   |             ☐ |       |

> ✅ Mark each column as you complete the step for every project.

## Notes & Best Practices

- **Prefer Migration to Modern .NET**: If on .NET Framework or .NET Standard, evaluate moving to .NET 8+ for long-term support.
- **Automate Tests Early**: CI/CD should block merges if tests fail.
- **Incremental Upgrades**: Large solutions may require upgrading one project at a time.
