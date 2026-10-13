"""Fetch everything PostHog has on an error-report incident: every lobby member's side of it.

    python tools/fetch-incident.py 7F3K-Q2MD
    python tools/fetch-incident.py --lobby lobby-1a2b3c4d --hours 6

Writes dist/incidents/<code>/ (gitignored):
    summary.txt         what happened: the incident, each member (role, version, where they were), errors, bug report
    <role>-<id>.log     each member's log lines around the incident (sent by the mod as incident_log events), plus
                        their full log for the minutes around it when PostHog's logs table can be read
    crash-*.log         a crash's last lines and the game's own log (a crash found at the next launch)
    events.json         every event, as PostHog has it

Needs a PostHog personal API key with read access to queries, in the environment, never in the repo:
    POSTHOG_PERSONAL_KEY   phx_...
    POSTHOG_PROJECT_ID     optional (default: the key's current project)
    POSTHOG_HOST           optional (default: https://us.posthog.com)
"""
import argparse
import json
import os
import re
import sys
import urllib.error
import urllib.request
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CODE = re.compile(r"^[23456789ABCDEFGHJKMNPQRSTUVWXYZ]{4}-[23456789ABCDEFGHJKMNPQRSTUVWXYZ]{4}$")  # IncidentRules' alphabet
LOBBY = re.compile(r"^lobby-[0-9a-f]{8}$")


def query(sql):
    key = os.environ.get("POSTHOG_PERSONAL_KEY", "").strip()
    if not key:
        sys.exit("Set POSTHOG_PERSONAL_KEY to a PostHog personal API key (phx_...) first.")
    host = os.environ.get("POSTHOG_HOST", "https://us.posthog.com").rstrip("/")
    project = os.environ.get("POSTHOG_PROJECT_ID", "@current")
    body = json.dumps({"query": {"kind": "HogQLQuery", "query": sql}}).encode()
    request = urllib.request.Request(f"{host}/api/projects/{project}/query/", data=body, method="POST",
                                     headers={"Authorization": f"Bearer {key}", "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(request, timeout=120) as response:
            result = json.load(response)
    except urllib.error.HTTPError as e:
        raise RuntimeError(f"PostHog said {e.code}: {e.read().decode(errors='replace')[:500]}") from None
    columns = result.get("columns") or []
    return [dict(zip(columns, row)) for row in result.get("results") or []]


def props(row):
    value = row.get("properties")
    return json.loads(value) if isinstance(value, str) else (value or {})


def member_name(p):
    role = p.get("role") or "unknown"
    return f"{role}-{(p.get('session_id') or 'session')[:6]}"


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("code", nargs="?", help="the incident code, like 7F3K-Q2MD")
    parser.add_argument("--lobby", help="a lobby's label instead (from summary.txt or a log line), like lobby-1a2b3c4d")
    parser.add_argument("--hours", type=float, default=24, help="with --lobby: how far back (default 24)")
    parser.add_argument("--days", type=int, default=30, help="how far back to look for the incident (default 30)")
    args = parser.parse_args()

    if args.code:
        code = args.code.strip().upper()
        if not CODE.match(code):
            sys.exit(f"{args.code!r} isn't an incident code (like 7F3K-Q2MD).")
        where = f"properties.incident_id = '{code}' AND timestamp > now() - INTERVAL {int(args.days)} DAY"
        name = code
    elif args.lobby:
        if not LOBBY.match(args.lobby):
            sys.exit(f"{args.lobby!r} isn't a lobby label (like lobby-1a2b3c4d).")
        where = f"properties.lobby = '{args.lobby}' AND timestamp > now() - INTERVAL {int(args.hours * 60)} MINUTE"
        name = args.lobby
    else:
        sys.exit("Give an incident code, or --lobby.")

    events = query(f"SELECT uuid, event, timestamp, distinct_id, properties FROM events WHERE {where} ORDER BY timestamp LIMIT 5000")
    if not events:
        sys.exit(f"Nothing found for {name}. (Reports arrive within a minute or so; a player with reports off sends none.)")
    out = ROOT / "dist" / "incidents" / name
    out.mkdir(parents=True, exist_ok=True)
    for row in events:
        row["properties"] = props(row)
    (out / "events.json").write_text(json.dumps(events, indent=2, default=str), encoding="utf-8")

    summary, logs, sessions = [], defaultdict(list), {}
    for row in events:
        p, event, when = row["properties"], row["event"], row["timestamp"]
        member = member_name(p)
        if p.get("session_id"):
            sessions[p["session_id"]] = member
        if event == "incident":
            summary.append(f"{when}  INCIDENT {p.get('incident_id')} on {member}: {p.get('trigger')}: {p.get('summary')}"
                           + ("" if p.get("is_new", True) else "  (joined an incident already going)"))
            if p.get("log_tail"):
                (out / f"crash-{member}.log").write_text(p["log_tail"], encoding="utf-8")
            if p.get("unity_log_tail"):
                (out / f"crash-{member}-unity.log").write_text(p["unity_log_tail"], encoding="utf-8")
        elif event == "incident_member":
            heard = f" (heard from {p['heard_from']})" if p.get("heard_from") else " (it happened here)"
            summary.append(f"{when}  member {member}{heard}: version {p.get('version')} {p.get('channel')}, phase {p.get('phase')}, "
                           f"level {p.get('level_index', '-')}, lobby {p.get('lobby', '-')}")
            for key in ("member_versions", "party_phase", "load_blocking", "map_waiting", "spectating", "load_step",
                        "mm_state_age_s", "mm_in_battle", "mm_wave", "mm_waves", "island_english", "island_vikings", "fps"):
                if key in p:
                    summary.append(f"        {key}: {p[key]}")
        elif event == "$exception":
            for exception in p.get("$exception_list") or []:
                frames = (exception.get("stacktrace") or {}).get("frames") or []
                top = [f.get("function") for f in reversed(frames)][:6]
                summary.append(f"{when}  ERROR on {member}: {exception.get('type')}: {exception.get('value')}")
                summary.extend(f"        at {function}" for function in top)
        elif event == "bug_report":
            summary.append(f"{when}  BUG REPORT from {member}: {p.get('description') or '(no description)'}")
        elif event == "incident_log":
            logs[member].append(("aftermath (30 s later)" if p.get("aftermath") else "at the incident", when, p.get("log") or ""))
        else:
            summary.append(f"{when}  {event} on {member}" + (f": {p.get('message')}" if p.get("message") else ""))

    for member, parts in logs.items():
        text = []
        for label, when, log in parts:
            text.append(f"===== {member}: {label} ({when}) =====")
            text.append(log)
        (out / f"{member}.log").write_text("\n".join(text) + "\n", encoding="utf-8")

    # The full log stream (PostHog Logs) for each member, a few minutes either side, where PostHog lets it be queried.
    full_note = ""
    if sessions:
        first, last = events[0]["timestamp"], events[-1]["timestamp"]
        ids = ", ".join(f"'{s}'" for s in sessions if re.match(r"^[0-9a-f]{12}$", s))
        try:
            lines = query(
                "SELECT timestamp, severity_text, body, attributes, resource_attributes FROM logs "
                f"WHERE resource_attributes['session_id'] IN ({ids}) "
                f"AND timestamp > toDateTime('{first[:19].replace('T', ' ')}') - INTERVAL 5 MINUTE "
                f"AND timestamp < toDateTime('{last[:19].replace('T', ' ')}') + INTERVAL 2 MINUTE "
                "ORDER BY timestamp LIMIT 50000")
            by_member = defaultdict(list)
            for line in lines:
                resource = line.get("resource_attributes") or {}
                if isinstance(resource, str):
                    resource = json.loads(resource)
                attributes = line.get("attributes") or {}
                if isinstance(attributes, str):
                    attributes = json.loads(attributes)
                member = sessions.get(resource.get("session_id"), "unknown")
                by_member[member].append(f"{line['timestamp']} [{line.get('severity_text')}: {attributes.get('source', '')}] {line.get('body')}")
            for member, member_lines in by_member.items():
                (out / f"{member}.full.log").write_text("\n".join(member_lines) + "\n", encoding="utf-8")
            full_note = f"Full logs: {sum(len(v) for v in by_member.values())} lines from PostHog Logs."
        except RuntimeError as e:
            full_note = f"Full logs not fetched (PostHog's logs table couldn't be queried: {str(e)[:200]}). The incident_log lines above are each member's recent log."

    header = [f"{name}: {len(events)} events from {len(sessions)} session(s)", full_note, ""]
    (out / "summary.txt").write_text("\n".join(header + summary) + "\n", encoding="utf-8")
    print("\n".join(header + summary[:60]))
    print(f"\nWritten to {out}")


if __name__ == "__main__":
    main()
