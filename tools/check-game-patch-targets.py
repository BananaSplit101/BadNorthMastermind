"""Check named Harmony targets against the installed game's actual CLR metadata.

This catches missing/private/explicit-interface targets without starting Unity.
It does not substitute for exercising patches inside the game.
"""
from pathlib import Path
import re
import dnfile

root = Path(__file__).resolve().parents[1]
managed = Path(r"C:\Program Files (x86)\Steam\steamapps\common\BadNorth\BadNorth_Data\Managed")
assembly = dnfile.dnPE(str(managed / "Assembly-CSharp.dll"))
types = {}
for entry in assembly.net.mdtables.TypeDef.rows:
    types.setdefault(str(entry.TypeName), set()).update(str(m.row.Name) for m in entry.MethodList)

checked = 0
for path in (root / "src").rglob("*.cs"):
    text = path.read_text(encoding="utf-8-sig")
    # Attribute targets and reflected targets share the same existence requirement.
    pattern = (r'(?:HarmonyPatch\(|AccessTools\.Method\()typeof\((\w+)\),\s*(?:nameof\(\w+\.(\w+)\)|"([^"]+)")'
               r'(?:\s*,\s*MethodType\.(Getter|Setter))?')
    for match in re.finditer(pattern, text):
        type_name, member, explicit, accessor = match.groups()
        member = member or explicit
        # Property patches target the compiler's accessor methods.
        if accessor: member = ("get_" if accessor == "Getter" else "set_") + member
        if type_name not in types:  # Mod-owned targets are checked by the native build.
            continue
        assert member in types[type_name], f"Missing game target {type_name}.{member} in {path}"
        checked += 1
assert checked >= 35, f"Unexpectedly few target checks: {checked}"
print(f"{checked} named Harmony/reflection targets exist in the installed game assembly.")
