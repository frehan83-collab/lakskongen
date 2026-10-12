"""
Sim Balance Probe — .NET Runner
Orchestrates headless simulation runs via dotnet CLI.
"""

import json
import subprocess
import tempfile
import os
from pathlib import Path
from typing import Dict, Any, Optional, List
from dataclasses import dataclass, asdict
from concurrent.futures import ProcessPoolExecutor, as_completed
import time

from .config import (
    REPO_ROOT,
    TEST_PROJECT,
    DOTNET_RUNNER_NAME,
    DOTNET_RUNNER_CODE,
    CAREER_HORIZON_DAYS,
    SAVE_LOAD_DAY,
    DEFAULT_PARALLEL_JOBS,
)


@dataclass
class RunResult:
    """Result of a single career run."""
    scenario: str
    seed: int
    success: bool
    metrics: Dict[str, Any]
    error: Optional[str] = None
    duration_sec: float = 0.0


class DotnetRunner:
    """Manages the .NET test runner compilation and execution."""

    def __init__(self, project_path: Optional[Path] = None):
        self.project_path = project_path or TEST_PROJECT
        self.runner_path: Optional[Path] = None
        self._compiled = False

    def ensure_compiled(self, configuration: str = "Release") -> bool:
        """Build the test project and ensure runner is available."""
        if self._compiled:
            return True

        print(f"[runner] Building {self.project_path} ({configuration})...")
        result = subprocess.run(
            ["dotnet", "build", str(self.project_path), "-c", configuration, "--no-restore"],
            cwd=REPO_ROOT,
            capture_output=True,
            text=True,
        )

        if result.returncode != 0:
            print(f"[runner] Build failed:\n{result.stdout}\n{result.stderr}")
            return False

        print(f"[runner] Build successful")
        self._compiled = True
        return True

    def run_career(
        self,
        scenario: str,
        seed: int,
        horizon_days: int = CAREER_HORIZON_DAYS,
        save_load: bool = True,
        configuration: str = "Release",
    ) -> RunResult:
        """Run a single career and return parsed metrics."""
        start_time = time.time()

        if not self.ensure_compiled(configuration):
            return RunResult(
                scenario=scenario,
                seed=seed,
                success=False,
                metrics={},
                error="Build failed",
                duration_sec=time.time() - start_time,
            )

        try:
            return self._run_via_console_app(scenario, seed, horizon_days, save_load, configuration)
        except Exception as e:
            return RunResult(
                scenario=scenario,
                seed=seed,
                success=False,
                metrics={},
                error=f"Runner exception: {e}",
                duration_sec=time.time() - start_time,
            )

    def _run_via_console_app(
        self,
        scenario: str,
        seed: int,
        horizon_days: int,
        save_load: bool,
        configuration: str,
    ) -> RunResult:
        """Run via a temporary console app that references the test assembly."""
        start_time = time.time()

        # Create runner project in repo's temp location to avoid relative path issues
        runner_dir = REPO_ROOT / "artifacts" / "balance_probe" / "runner"
        runner_dir.mkdir(parents=True, exist_ok=True)
        
        runner_cs = runner_dir / f"{DOTNET_RUNNER_NAME}.cs"
        runner_cs.write_text(DOTNET_RUNNER_CODE)

        # Create a minimal csproj with absolute project reference
        csproj = runner_dir / f"{DOTNET_RUNNER_NAME}.csproj"
        csproj.write_text(f'''
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="{self.project_path}" />
  </ItemGroup>
</Project>
''')

        # Build the runner
        build_result = subprocess.run(
            ["dotnet", "build", str(csproj), "-c", configuration],
            cwd=REPO_ROOT,
            capture_output=True,
            text=True,
        )

        if build_result.returncode != 0:
            return RunResult(
                scenario=scenario,
                seed=seed,
                success=False,
                metrics={},
                error=f"Runner build failed: {build_result.stderr}",
                duration_sec=time.time() - start_time,
            )

        # Find the built dll
        dll_path = list(runner_dir.glob(f"bin/{configuration}/net10.0/{DOTNET_RUNNER_NAME}.dll"))
        if not dll_path:
            return RunResult(
                scenario=scenario,
                seed=seed,
                success=False,
                metrics={},
                error="Runner dll not found after build",
                duration_sec=time.time() - start_time,
            )

        dll = dll_path[0]

        # Run the career via dotnet exec (framework-dependent)
        run_result = subprocess.run(
            ["dotnet", "exec", str(dll), scenario, str(seed), str(horizon_days), str(save_load).lower()],
            cwd=REPO_ROOT,
            capture_output=True,
            text=True,
            timeout=300,  # 5 min per career
        )

        duration = time.time() - start_time

        if run_result.returncode != 0:
            return RunResult(
                scenario=scenario,
                seed=seed,
                success=False,
                metrics={},
                error=f"Run failed (code {run_result.returncode}): {run_result.stderr[:500]}",
                duration_sec=duration,
            )

        # Parse JSON output (last line should be the JSON)
        try:
            stdout_lines = run_result.stdout.strip().split('\n')
            json_line = stdout_lines[-1] if stdout_lines else ""
            metrics = json.loads(json_line)
            return RunResult(
                scenario=scenario,
                seed=seed,
                success=True,
                metrics=metrics,
                duration_sec=duration,
            )
        except json.JSONDecodeError as e:
            return RunResult(
                scenario=scenario,
                seed=seed,
                success=False,
                metrics={},
                error=f"JSON parse error: {e}. Output: {run_result.stdout[:500]}",
                duration_sec=duration,
            )


def run_scenario_seeds(
    scenario: str,
    seeds: List[int],
    horizon_days: int = CAREER_HORIZON_DAYS,
    save_load: bool = True,
    parallel_jobs: int = DEFAULT_PARALLEL_JOBS,
    configuration: str = "Release",
) -> List[RunResult]:
    """Run multiple seeds for a single scenario in parallel."""
    runner = DotnetRunner()
    results = []

    print(f"[runner] Running {scenario}: {len(seeds)} seeds (parallel={parallel_jobs})")

    with ProcessPoolExecutor(max_workers=parallel_jobs) as executor:
        futures = {
            executor.submit(
                _run_single_career,
                scenario,
                seed,
                horizon_days,
                save_load,
                configuration,
            ): seed
            for seed in seeds
        }

        for future in as_completed(futures):
            seed = futures[future]
            try:
                result = future.result()
                results.append(result)
                status = "✓" if result.success else "✗"
                print(f"  [{status}] seed={seed} ({result.duration_sec:.1f}s)")
            except Exception as e:
                results.append(RunResult(
                    scenario=scenario,
                    seed=seed,
                    success=False,
                    metrics={},
                    error=f"Executor error: {e}",
                    duration_sec=0.0,
                ))
                print(f"  [✗] seed={seed} (exception: {e})")

    return results


def _run_single_career(
    scenario: str,
    seed: int,
    horizon_days: int,
    save_load: bool,
    configuration: str,
) -> RunResult:
    """Helper for parallel execution - creates own runner instance."""
    runner = DotnetRunner()
    return runner.run_career(scenario, seed, horizon_days, save_load, configuration)


def run_all_scenarios(
    scenarios: List[str],
    seeds_per_scenario: int,
    seed_start: int = 1,
    horizon_days: int = CAREER_HORIZON_DAYS,
    save_load: bool = True,
    parallel_jobs: int = DEFAULT_PARALLEL_JOBS,
    configuration: str = "Release",
) -> Dict[str, List[RunResult]]:
    """Run all scenarios × seeds."""
    all_results = {}

    for scenario in scenarios:
        seeds = list(range(seed_start, seed_start + seeds_per_scenario))
        results = run_scenario_seeds(
            scenario=scenario,
            seeds=seeds,
            horizon_days=horizon_days,
            save_load=save_load,
            parallel_jobs=parallel_jobs,
            configuration=configuration,
        )
        all_results[scenario] = results

    return all_results