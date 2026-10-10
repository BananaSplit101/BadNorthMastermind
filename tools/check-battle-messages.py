"""Every message sent through BattleStream must be routed as a battle message.

Router.IsBattleMessage only hands SquadSpawn..LastBattleMessage to the puppet world; anything else sent
through BattleStream arrives as "No handler for message ..." on watchers (the 0.10.5 SquadTiles bug).
"""
from pathlib import Path
import re

root = Path(__file__).resolve().parents[1]
protocol = (root / "src" / "Net" / "Protocol.cs").read_text(encoding="utf-8-sig")
values = {name: int(value) for name, value in re.findall(r"^\s*(\w+)\s*=\s*(\d+)\s*,", protocol, re.M)}
low, high = values["SquadSpawn"], values["LastBattleMessage"]

checked = 0
for path in (root / "src").rglob("*.cs"):
    text = path.read_text(encoding="utf-8-sig")
    for match in re.finditer(r"BattleStream\.Send\(\s*Packet\.Build\(\s*MsgType\.(\w+)", text):
        name = match.group(1)
        assert name in values, f"Unknown message {name} in {path}"
        assert low <= values[name] <= high, (
            f"{name} = {values[name]} is sent through BattleStream in {path.name} but Router only routes "
            f"{low}..{high} as battle messages")
        checked += 1
assert checked >= 15, f"Unexpectedly few battle messages found: {checked}"
print(f"{checked} battle-stream sends use message types inside {low}..{high}.")
