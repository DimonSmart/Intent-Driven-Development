#!/usr/bin/env python3
"""Install registered IDD Factory roles from this Codex plugin into a Codex config layer.

Codex 0.167.1 does not load plugin-supplied agents from the Marketplace manifest.
Registration is an explicit, user-authorized step; never edit Codex config or trust.
"""
import argparse
import hashlib
import os
from pathlib import Path
import sys
import tempfile

ROLES = ("idd-internal-factory-planner", "idd-internal-factory-worker")


def resolve_paths(args):
    plugin_root = Path(__file__).resolve().parents[3]
    sources = [plugin_root / "agents" / (name + ".toml") for name in ROLES]
    if any(not source.is_file() for source in sources):
        raise ValueError("The installed Factory plugin is incomplete: missing native agent TOML.")
    if args.scope == "project":
        if args.codex_home:
            raise ValueError("--codex-home only applies to --scope user")
        project = Path(args.project or os.getcwd()).expanduser().resolve()
        if not project.is_dir():
            raise ValueError("Project directory does not exist: " + str(project))
        target_dir = project / ".codex" / "agents"
    else:
        if args.project:
            raise ValueError("--project only applies to --scope project")
        home = Path(args.codex_home or os.environ.get("CODEX_HOME") or Path.home() / ".codex")
        target_dir = home.expanduser().resolve() / "agents"
    return sources, target_dir


def preflight(sources, target_dir, replace):
    if target_dir.is_symlink() or target_dir.parent.is_symlink():
        raise ValueError("Refusing to write through a symlinked agent/config directory.")
    if target_dir.exists() and not target_dir.is_dir():
        raise ValueError("Agent destination exists but is not a directory: " + str(target_dir))
    planned = []
    for source in sources:
        destination = target_dir / source.name
        if destination.is_symlink() or (destination.exists() and not destination.is_file()):
            raise ValueError("Refusing a symlink or non-file agent destination: " + str(destination))
        new_bytes = source.read_bytes()
        old_bytes = destination.read_bytes() if destination.exists() else None
        if old_bytes == new_bytes:
            action, backup = "current", None
        elif old_bytes is None:
            action, backup = "install", None
        else:
            if not replace:
                action, backup = "conflict", None
            else:
                digest = hashlib.sha256(old_bytes).hexdigest()[:12]
                backup = destination.with_name(destination.name + ".idd-backup-" + digest + ".bak")
                if backup.is_symlink() or (backup.exists() and backup.read_bytes() != old_bytes):
                    raise ValueError("Backup name already occupied by different content: " + str(backup))
                action = "replace"
        planned.append((destination, new_bytes, old_bytes, action, backup))
    return planned


def atomic_write(destination, content):
    fd, temp_name = tempfile.mkstemp(prefix=".idd-factory-", dir=destination.parent)
    try:
        with os.fdopen(fd, "wb") as stream:
            stream.write(content)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temp_name, destination)
    finally:
        if os.path.exists(temp_name):
            os.unlink(temp_name)


def main(argv=None):
    parser = argparse.ArgumentParser(description="Register installed IDD Factory native agents for Codex.")
    parser.add_argument("--scope", choices=("project", "user"), required=True)
    parser.add_argument("--project", help="Project root for project-scope roles; defaults to cwd.")
    parser.add_argument("--codex-home", help="Override CODEX_HOME for user-scope roles.")
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--check", action="store_true", help="Read-only check; exit 2 if installation/update needed.")
    mode.add_argument("--apply", action="store_true", help="Apply authorized registration.")
    parser.add_argument("--replace", action="store_true",
                        help="Explicitly replace conflicting role files, keeping content-addressed backups.")
    args = parser.parse_args(argv)
    try:
        sources, target_dir = resolve_paths(args)
        planned = preflight(sources, target_dir, args.replace)
        for destination, _, _, action, backup in planned:
            print(action.upper() + ": " + str(destination))
            if backup:
                print("  backup: " + str(backup))
        if any(item[3] == "conflict" for item in planned):
            print("CONFLICT: existing roles differ. Review them; use --replace only with explicit approval.",
                  file=sys.stderr)
            return 3
        if args.check:
            return 0 if all(item[3] == "current" for item in planned) else 2
        target_dir.mkdir(parents=True, exist_ok=True)
        for destination, new_bytes, old_bytes, action, backup in planned:
            if action == "current":
                continue
            if action == "replace" and backup is not None and not backup.exists():
                atomic_write(backup, old_bytes)
            atomic_write(destination, new_bytes)
        print("Registered roles from installed plugin. Start a NEW Codex session to reload native roles.")
        if args.scope == "project":
            print("Project-scope roles require a trusted Codex project. Trust is NOT modified by this tool.")
        print("Native agent availability and instruction loading still require a real host spawn probe.")
        return 0
    except (OSError, ValueError) as exc:
        print("ERROR: " + str(exc), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
