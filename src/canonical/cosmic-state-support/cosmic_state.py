#!/usr/bin/env python3
"""Optional deterministic COSMIC run-state helper. Never classifies FUR semantics."""
from __future__ import annotations

import argparse
from collections import Counter, defaultdict
import hashlib
import json
import os
from pathlib import Path
import re
import sys
import tempfile

SPEC = re.compile(r"IDD-\d{4}\.spec-[A-Za-z0-9_-]+\.md$")
OWNER = re.compile(r"IDD-\d{4}#P[1-9]\d*$")
VERSION = "owner-links-v1"


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def contained(path: Path, root: Path) -> bool:
    return path == root or root in path.parents


def inventory(project: Path) -> dict[str, str]:
    folder = project / ".idd" / "intent"
    require(folder.is_dir(), f"Missing intent folder: {folder}")
    return {p.name: digest(p) for p in sorted(folder.iterdir())
            if p.is_file() and SPEC.fullmatch(p.name)}


def write_atomic(path: Path, value: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, name = tempfile.mkstemp(prefix=".cosmic-", suffix=".tmp", dir=path.parent)
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as stream:
            json.dump(value, stream, indent=2, ensure_ascii=False, sort_keys=True)
            stream.write("\n")
        os.replace(name, path)
    finally:
        if os.path.exists(name):
            os.unlink(name)


def load(state_dir: Path) -> dict:
    path = state_dir / "run.json"
    require(path.is_file(), f"Missing run state: {path}")
    data = json.loads(path.read_text(encoding="utf-8"))
    require(data.get("schema") == 1 and data.get("protocol") == VERSION,
            "Incompatible run state schema/protocol")
    project = Path(data["project"]).resolve()
    require(not contained(state_dir.resolve(), project), "State must be outside project")
    return data


def stale(data: dict) -> dict[str, str]:
    current = inventory(Path(data["project"]))
    old = data["sources"]
    problems = {p: "new" for p in current.keys() - old.keys()}
    problems.update({p: "removed" for p in old.keys() - current.keys()})
    problems.update({p: "changed" for p in current.keys() & old.keys()
                     if current[p] != old[p]["sha256"]})
    for name in old.keys() & current.keys():
        item = old[name]
        if item["status"] == "Completed":
            packet = item.get("packet")
            p = Path(data["state_dir"]) / packet if packet else None
            if not p or not p.is_file() or digest(p) != item.get("packet_sha256"):
                problems[name] = "missing-or-changed-packet"
    return problems


def parse_records(path: Path) -> tuple[dict, list, list, list]:
    owners: dict[str, tuple[str, str]] = {}
    links: list[tuple[str, str, str, str]] = []
    moves: list[tuple[str, str, str, str]] = []
    measures: list[tuple[str, str, dict, int]] = []
    for lineno, raw in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        if not raw.strip() or raw.lstrip().startswith("#"):
            continue
        parts = [p.strip() for p in raw.split("|")]
        kind = parts[0]
        require(kind in {"OWNER", "LINK", "MOVE", "MEASURE"},
                f"Line {lineno}: unknown record {kind}")
        require(len(parts) == (4 if kind == "OWNER" else 5),
                f"Line {lineno}: wrong number of fields")
        require(all(parts), f"Line {lineno}: empty field")
        if kind == "OWNER":
            key, filename, anchor = parts[1:]
            require(OWNER.fullmatch(key) is not None, f"Line {lineno}: invalid owner ID")
            require(key not in owners, f"Line {lineno}: duplicate owner ID {key}")
            require(filename.startswith(key[:8]) and SPEC.fullmatch(filename),
                    f"Line {lineno}: owner source does not match ID")
            owners[key] = (filename, anchor)
        elif kind == "LINK":
            links.append(tuple(parts[1:]))
        elif kind == "MOVE":
            key, movement, group, anchor = parts[1:]
            require(movement in {"E", "X", "R", "W"}, f"Line {lineno}: invalid move")
            moves.append((key, movement, group, anchor))
        else:
            key, boundary, totals, cfp = parts[1:]
            counts = {}
            for item in totals.split(","):
                keyvalue = item.strip().split("=")
                require(len(keyvalue) == 2 and keyvalue[0] in {"E", "X", "R", "W"}
                        and keyvalue[0] not in counts and keyvalue[1].isdigit(),
                        f"Line {lineno}: invalid movement counts")
                counts[keyvalue[0]] = int(keyvalue[1])
            require(set(counts) == {"E", "X", "R", "W"},
                    f"Line {lineno}: incomplete movement counts")
            require(re.fullmatch(r"CFP=\d+", cfp) is not None,
                    f"Line {lineno}: invalid CFP field")
            measures.append((key, boundary, counts, int(cfp[4:])))
    return owners, links, moves, measures


def audit(data: dict, records: Path, complete: bool) -> dict:
    errors = []
    old = data["sources"]
    differences = stale(data)
    errors += [f"stale:{name}:{reason}" for name, reason in sorted(differences.items())]
    if complete:
        errors += [f"not-completed:{name}" for name, item in sorted(old.items())
                   if item["status"] != "Completed"]
    owners, links, moves, measures = parse_records(records)
    errors += [f"unknown-owner-source:{key}" for key, (src, _) in owners.items()
               if src not in old]
    for key, typ, _, _ in moves:
        if key not in owners:
            errors.append(f"movement-unknown-owner:{key}")
    occurrences = Counter(key for key, _, _, _ in measures)
    errors += [f"duplicate-measure:{key}" for key, n in occurrences.items() if n > 1]
    for key, boundary, counts, cfp in measures:
        if key not in owners:
            errors.append(f"measure-unknown-owner:{key}")
        if boundary != data["boundary"]:
            errors.append(f"incompatible-boundary:{key}")
        movement_counts = Counter(typ for owner, typ, _, _ in moves if owner == key)
        if any(counts[typ] != movement_counts[typ] for typ in counts):
            errors.append(f"movement-count-mismatch:{key}")
        if cfp != sum(counts.values()):
            errors.append(f"cfp-arithmetic:{key}")
        if counts["E"] < 1 or counts["X"] + counts["W"] < 1:
            errors.append(f"cosmic-rule-10c:{key}")
    if complete:
        errors += [f"unmeasured-owner:{key}" for key in owners if occurrences[key] == 0]
        if not owners:
            errors.append("empty-scope-requires-separate-semantic-proof")
    incoming: dict[str, set[str]] = defaultdict(set)
    referring: dict[str, set[str]] = defaultdict(set)
    for caller, target, source, anchor in links:
        if target not in owners:
            errors.append(f"unresolved-link-target:{target}")
        if source not in old:
            errors.append(f"unknown-link-source:{source}")
        if caller.startswith("SRC:"):
            if caller[4:] != source:
                errors.append(f"invalid-source-only-caller:{caller}")
        elif caller not in owners:
            errors.append(f"unknown-link-caller:{caller}")
        elif owners[caller][0] != source:
            errors.append(f"caller-source-mismatch:{caller}")
        if caller == target:
            errors.append(f"self-link:{caller}")
        if target in owners:
            referring[target].add(source)
            if not caller.startswith("SRC:"):
                incoming[target].add(caller)
    return {"valid": len(errors) == 0, "errors": sorted(set(errors)),
            "covered": sum(x["status"] == "Completed" for x in old.values()),
            "sources": len(old), "owned_processes": len(owners),
            "measured_processes": len(occurrences),
            "total_cfp": sum(x[3] for x in measures) if not errors else None,
            "coupling": {key: {"confirmed_callers": len(incoming[key]),
                                "referring_specs": len(referring[key])}
                         for key in sorted(owners)}}


def change_dependent_sources(data: dict, records: Path | None) -> set[str]:
    changed = set(stale(data))
    if not records or not records.is_file():
        return changed
    owners, links, _, _ = parse_records(records)
    dependents: dict[str, set[str]] = defaultdict(set)
    for _caller, target, source, _anchor in links:
        if target in owners:
            dependents[owners[target][0]].add(source)
    pending = list(changed)
    while pending:
        for dependent in dependents.get(pending.pop(), set()):
            if dependent not in changed:
                changed.add(dependent)
                pending.append(dependent)
    return changed


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["init", "mark", "status", "invalidate", "verify"])
    parser.add_argument("--project")
    parser.add_argument("--state-dir", required=True)
    parser.add_argument("--boundary")
    parser.add_argument("--source")
    parser.add_argument("--packet")
    parser.add_argument("--records")
    parser.add_argument("--complete", action="store_true")
    args = parser.parse_args()
    state_dir = Path(args.state_dir).resolve()
    state_path = state_dir / "run.json"
    if args.command == "init":
        require(args.project and args.boundary, "init requires project and boundary")
        project = Path(args.project).resolve()
        require(not contained(state_dir, project), "State directory must be outside project")
        require(not state_path.exists(), "Existing state: use status/invalidate rather than resetting")
        data = {"schema": 1, "protocol": VERSION, "edition": "COSMIC 5.0",
                "project": str(project), "state_dir": str(state_dir),
                "boundary": args.boundary,
                "sources": {name: {"sha256": sha, "status": "Pending", "packet": None,
                                   "packet_sha256": None}
                            for name, sha in inventory(project).items()}}
        write_atomic(state_path, data)
        result = {"initialized": len(data["sources"]), "state": str(state_path)}
    else:
        data = load(state_dir)
        if args.boundary is not None:
            require(args.boundary == data["boundary"], "Boundary differs from saved run")
        if args.command == "status":
            result = {"stale": stale(data), "statuses": {name: x["status"]
                     for name, x in data["sources"].items()}}
        elif args.command == "mark":
            require(args.source and args.packet, "mark requires source and packet")
            name = args.source
            require(name in data["sources"], f"Unknown source {name}")
            require(name not in stale(data), f"Stale source or packet: {name}")
            packet = Path(args.packet).resolve()
            require(contained(packet, state_dir) and packet.is_file(),
                    "Evidence packet must exist inside state directory")
            content = packet.read_text(encoding="utf-8")
            require(f"Source: {name}" in content and "Coverage: Read in full" in content,
                    "Packet must identify source and completed read (semantic proof checked by agent)")
            data["sources"][name].update(status="Completed", packet=str(packet.relative_to(state_dir)),
                                         packet_sha256=digest(packet))
            write_atomic(state_path, data)
            result = {"completed": name}
        elif args.command == "invalidate":
            invalidated = change_dependent_sources(data,
                        Path(args.records).resolve() if args.records else None)
            current = inventory(Path(data["project"]))
            require(current.keys() == data["sources"].keys(),
                    "Source inventory changed; initialize a new run for the new scope")
            for name, sha in current.items():
                if name in invalidated:
                    data["sources"][name].update(sha256=sha, status="Invalidated",
                                                packet=None, packet_sha256=None)
            write_atomic(state_path, data)
            result = {"invalidated": sorted(invalidated),
                      "pending": sorted(name for name, x in data["sources"].items()
                                        if x["status"] in {"Pending", "Invalidated"})}
        else:
            require(args.records, "verify requires --records")
            result = audit(data, Path(args.records).resolve(), args.complete)
    print(json.dumps(result, indent=2, sort_keys=True))
    return 0 if result.get("valid", True) else 2


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (ValueError, KeyError, OSError, json.JSONDecodeError) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        sys.exit(2)
