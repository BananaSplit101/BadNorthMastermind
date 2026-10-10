"""Exercise the actual C# gate expression against scene-state combinations."""
from pathlib import Path
import itertools
import re
source = (Path(__file__).resolve().parents[1] / "src/Spectator/SpectatorSession.cs").read_text()
method = source.split("private static bool AtMainMenu()", 1)[1].split("public static bool IslandReady", 1)[0]
expression = re.search(r"return (.*?);", method, re.S).group(1)
for old, new in [
    ("stack.stateLoading.gameObject.activeInHierarchy", "loading"),
    ("stack.stateLevel.gameObject.activeInHierarchy", "level"),
    ("campaigns.campaign", "campaign"),
    ("campaigns", "manager"),
    ("&&", " and "), ("||", " or "), ("!", "not ")]:
    expression = expression.replace(old, new)
for manager, campaign, stack, loading, level in itertools.product([False, True], repeat=5):
    result = eval(" ".join(expression.split()), {"__builtins__": {}}, locals())
    expected = stack and not loading and not level and (not manager or not campaign)
    assert result == expected, (manager, campaign, stack, loading, level)
assert "CampaignManager.GenerateCampaign(save, campaignIsNew: false)" in source
print("32 spectator gate cases passed, including the fresh menu without a campaign manager.")

