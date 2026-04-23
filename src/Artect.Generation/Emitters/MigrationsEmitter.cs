using System.Collections.Generic;
using Artect.Config;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits <c>scripts/add-initial-migration.ps1</c> and <c>scripts/add-initial-migration.sh</c>
/// at the scaffold root.
/// Only runs when <c>cfg.GenerateInitialMigration == true &amp;&amp; cfg.DataAccess == EfCore</c>
/// (Dapper does not use EF migrations — PRD FR-37).
/// </summary>
public sealed class MigrationsEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var cfg = ctx.Config;

        if (!cfg.GenerateInitialMigration || cfg.DataAccess != DataAccessKind.EfCore)
            return System.Array.Empty<EmittedFile>();

        var project = cfg.ProjectName;
        var infraProject = CleanLayout.InfrastructureProjectName(project);
        var apiProject   = CleanLayout.ApiProjectName(project);

        return new[]
        {
            new EmittedFile("scripts/add-initial-migration.ps1",
                BuildPowerShell(project, infraProject, apiProject)),
            new EmittedFile("scripts/add-initial-migration.sh",
                BuildBash(project, infraProject, apiProject)),
        };
    }

    // ── PowerShell ─────────────────────────────────────────────────────────

    static string BuildPowerShell(string project, string infraProject, string apiProject) => $"""
        # add-initial-migration.ps1
        #
        # Flow:
        #   1. Ensures dotnet-ef global tool is installed.
        #   2. Adds the 'InitialCreate' EF Core migration to {infraProject}.
        #   3. Generates an idempotent SQL script → migrations/initial.sql.
        #      Run that script against your database to bootstrap the
        #      __EFMigrationsHistory table and apply the initial schema.
        #
        # Usage: .\scripts\add-initial-migration.ps1
        #        (run from the repository root)

        Set-StrictMode -Version Latest
        $ErrorActionPreference = 'Stop'

        Write-Host "Installing dotnet-ef (skips if already installed)..."
        dotnet tool install --global dotnet-ef 2>$null; $LASTEXITCODE = 0

        Write-Host "Adding InitialCreate migration..."
        dotnet ef migrations add InitialCreate `
            --project src/{infraProject} `
            --startup-project src/{apiProject}

        Write-Host "Generating idempotent SQL script → migrations/initial.sql..."
        New-Item -ItemType Directory -Force -Path migrations | Out-Null
        dotnet ef migrations script --idempotent `
            --project src/{infraProject} `
            --startup-project src/{apiProject} `
            --output migrations/initial.sql

        Write-Host "Done. Apply migrations/initial.sql to your database to bootstrap the schema."
        """;

    // ── Bash ───────────────────────────────────────────────────────────────

    static string BuildBash(string project, string infraProject, string apiProject) => $"""
        #!/usr/bin/env bash
        # add-initial-migration.sh
        #
        # Flow:
        #   1. Ensures dotnet-ef global tool is installed.
        #   2. Adds the 'InitialCreate' EF Core migration to {infraProject}.
        #   3. Generates an idempotent SQL script → migrations/initial.sql.
        #      Run that script against your database to bootstrap the
        #      __EFMigrationsHistory table and apply the initial schema.
        #
        # Usage: bash scripts/add-initial-migration.sh
        #        (run from the repository root)

        set -euo pipefail

        echo "Installing dotnet-ef (skips if already installed)..."
        dotnet tool install --global dotnet-ef 2>/dev/null || true

        echo "Adding InitialCreate migration..."
        dotnet ef migrations add InitialCreate \
            --project src/{infraProject} \
            --startup-project src/{apiProject}

        echo "Generating idempotent SQL script → migrations/initial.sql..."
        mkdir -p migrations
        dotnet ef migrations script --idempotent \
            --project src/{infraProject} \
            --startup-project src/{apiProject} \
            --output migrations/initial.sql

        echo "Done. Apply migrations/initial.sql to your database to bootstrap the schema."
        """;
}
