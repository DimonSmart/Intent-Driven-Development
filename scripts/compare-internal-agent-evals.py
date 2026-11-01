#!/usr/bin/env python3
"""Compare retained native eval traces without estimating missing token counters."""
import argparse
import datetime
import json
from pathlib import Path
import re
import statistics
import xml.etree.ElementTree as ET


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def timestamp(value):
    return datetime.datetime.fromisoformat(value.replace("Z", "+00:00"))


def inspect_run(path, expected_skills=None):
    path = Path(path).resolve()
    trace_root = path / "report-source" / "sessions"
    report_path = path / "factory-report.json"
    report = read_json(report_path) if report_path.exists() else {}
    roles = {a["threadId"]: a["role"] for a in report.get("agents", [])}
    rows = []
    response_owners = {}
    overlapping_responses = set()
    for trace in sorted(trace_root.rglob("*.jsonl")):
        records = [json.loads(line) for line in trace.read_text().splitlines() if line.strip()]
        meta = next(d["payload"] for d in records if d["type"] == "session_meta")
        thread_id = meta.get("id", meta.get("session_id"))
        usage = None
        response_usage = {}
        context = {}
        commands = []
        spawns = []
        visible_outputs = []
        for d in records:
            p = d.get("payload", {})
            if d["type"] == "turn_context":
                context = p
            if d["type"] == "token_usage_record":
                usage = p.get("thread_token_usage")
                response_id = p.get("response_id")
                if response_id and p.get("usage"):
                    response_usage[response_id] = p["usage"]
                    previous = response_owners.setdefault(response_id, thread_id)
                    if previous != thread_id:
                        overlapping_responses.add(response_id)
            if p.get("type") == "custom_tool_call_output":
                output = p.get("output", [])
                if isinstance(output, list):
                    for block in output:
                        if not isinstance(block, dict):
                            continue
                        text = block.get("text", "")
                        visible_outputs.append(text)
                        try:
                            decoded = json.loads(text)
                            if isinstance(decoded, dict) and isinstance(decoded.get("output"), str):
                                visible_outputs.append(decoded["output"])
                        except (ValueError, TypeError):
                            pass
            if d["type"] == "response_item" and p.get("type") == "function_call" and p.get("name") == "spawn_agent":
                args = json.loads(p["arguments"])
                spawns.append({k: args.get(k) for k in ("task_name", "fork_turns", "model", "reasoning_effort")})
            item = p.get("item", {})
            if item.get("type") == "CommandExecution":
                commands.append({"command": item.get("command"), "output": item.get("aggregated_output", ""),
                                 "exit_code": item.get("exit_code"), "ordinal": d.get("ordinal")})
        role = roles.get(thread_id, "child" if meta.get("parent_thread_id") else "root")
        source = str(meta.get("source", "")) + str(meta.get("thread_source", ""))
        if role == "child":
            role = "planner" if "planner" in source else "worker" if "worker" in source else "child"
        skill_reads = []
        for command in commands:
            text = str(command["command"])
            if re.search(r"/idd-internal-factory-(planner|worker)/SKILL\.md", text):
                # Output with both the protocol opening and ending establishes full
                # delivery in this tool result; truncated reads stay unverified.
                out = command["output"]
                skill_reads.append({"ordinal": command["ordinal"], "command": command["command"],
                                    "exit_code": command["exit_code"], "output_characters": len(out),
                                    "protocol_opening": "You are the Factory " in out,
                                    "protocol_inputs": "## Inputs" in out,
                                    "output_truncated": "truncated" in out.lower()})
        full_skills = []
        for name, expected in (expected_skills or {}).items():
            if any(expected.strip() in out.replace("\r\n", "\n") for out in visible_outputs):
                full_skills.append(name)
        counters_verified = bool(response_usage) and usage is not None and all(
            sum(item[key] for item in response_usage.values()) == usage[key]
            for key in ("input_tokens", "cached_input_tokens", "output_tokens"))
        tokens = None if not counters_verified else {
            "input": usage["input_tokens"], "cached_input": usage["cached_input_tokens"],
            "new_input": usage["input_tokens"] - usage["cached_input_tokens"],
            "output": usage["output_tokens"], "total": usage["input_tokens"] + usage["output_tokens"]}
        rows.append({"thread_id": thread_id, "parent_thread_id": meta.get("parent_thread_id"), "role": role,
                     "model": context.get("model"), "effort": context.get("effort"),
                     "multi_agent_version": context.get("multi_agent_version"), "tokens": tokens,
                     "native_response_count": len(response_usage), "response_counters_match_thread_total": counters_verified,
                     "duration_seconds": (timestamp(records[-1]["timestamp"]) - timestamp(records[0]["timestamp"])).total_seconds(),
                     "commands": len(commands), "spawns": spawns, "skill_reads": skill_reads,
                     "full_skill_in_model_visible_tool_output": full_skills,
                     "trace": str(trace.relative_to(path))})
    complete_tokens = bool(rows) and not overlapping_responses and all(row["tokens"] is not None for row in rows)
    totals = {key: sum(row["tokens"][key] for row in rows) for key in ("input", "cached_input", "new_input", "output", "total")} if complete_tokens else None
    root = next((row for row in rows if row["role"] == "root"), None)
    active_children = [row for row in rows if row["role"] != "root" and row["model"]]
    loading = {
        "root_read_internal_skill_bodies": bool(root and root["skill_reads"]),
        "active_children": len(active_children),
        "children_with_full_skill_tool_output": sum(bool(row["full_skill_in_model_visible_tool_output"]) for row in active_children),
        "every_spawn_explicitly_fresh": all(s["fork_turns"] == "none" for row in rows for s in row["spawns"]),
    }
    trx = list(path.glob("live-tests.trx"))
    outcomes = []
    for item in trx:
        outcomes.extend(e.attrib.get("outcome") for e in ET.parse(item).iter() if e.tag.endswith("UnitTestResult"))
    summary_path = path / "summary.json"
    summary = read_json(summary_path) if summary_path.exists() else {}
    elapsed = None
    if summary.get("startedAtUtc") and summary.get("finishedAtUtc"):
        elapsed = (timestamp(summary["finishedAtUtc"]) - timestamp(summary["startedAtUtc"])).total_seconds()
    return {"artifact": str(path), "test_outcomes": outcomes, "summary": summary,
            "result": report.get("run", {}).get("result", "unavailable"), "root": root,
            "eval_duration_seconds": elapsed,
            "total_tokens": totals, "agents": rows, "loading_evidence": loading,
            "cross_thread_response_overlap": sorted(overlapping_responses),
            "limitations": ["Encrypted spawn message bodies are not decoded; their contents remain unverified.",
                            "Costs are unavailable; cached input is a subset of input and is never added twice.",
                            "Timing includes concurrent host load; failed runs are not evidence of efficiency."]}


def aggregate(runs):
    passed = [r for r in runs if r["summary"].get("status") == "PASSED"
              and r["test_outcomes"] and all(o == "Passed" for o in r["test_outcomes"])]
    values = {}
    for name, getter in {
        "root_input": lambda r: r["root"]["tokens"]["input"],
        "root_output": lambda r: r["root"]["tokens"]["output"],
        "root_new_input": lambda r: r["root"]["tokens"]["new_input"],
        "root_duration_seconds": lambda r: r["root"]["duration_seconds"],
        "eval_duration_seconds": lambda r: r["eval_duration_seconds"],
        "total_tokens": lambda r: r["total_tokens"]["total"],
        "total_new_input": lambda r: r["total_tokens"]["new_input"],
    }.items():
        observed = []
        for run in passed:
            try:
                value = getter(run)
                if value is not None:
                    observed.append(value)
            except (TypeError, KeyError):
                pass
        values[name] = ({"n": len(observed), "median": statistics.median(observed),
                         "min": min(observed), "max": max(observed)} if observed else None)
    return {"attempted": len(runs), "passed": len(passed), "successful_run_metrics": values}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", action="append", required=True)
    parser.add_argument("--candidate", action="append", required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--candidate-marketplace", type=Path)
    args = parser.parse_args()
    expected = {}
    if args.candidate_marketplace:
        for role in ("planner", "worker"):
            name = "idd-internal-factory-" + role
            expected[name] = (args.candidate_marketplace / "plugins" / "codex" / "idd-factory" / "skills" / name / "SKILL.md").read_text()
    data = {name: [inspect_run(path, expected) for path in getattr(args, name)] for name in ("baseline", "candidate")}
    data["aggregates"] = {name: aggregate(data[name]) for name in ("baseline", "candidate")}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(data, indent=2) + "\n")
    print(json.dumps(data["aggregates"], indent=2))


if __name__ == "__main__":
    main()
