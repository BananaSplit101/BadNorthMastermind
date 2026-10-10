using BadNorthMP.Net;
using BadNorthMP.Spectator;
using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthMP.Game
{
    // Only attached to authoritative custom ships, never spectator copies.
    internal class CustomShipWatch : MonoBehaviour
    {
        private Longship ship;
        private float arrivedAt = -1f, landedAt = -1f, launchedAt = -1f;
        private bool reported, launched, launchRecovered;
        private void Awake()
        {
            ship = GetComponent<Longship>();
            if (ship) ship.onShipLaunch += OnLaunch;
        }
        private void OnDestroy() { if (ship) ship.onShipLaunch -= OnLaunch; }
        // A squad fleeing on this boat: it sails off once the launch clip's event re-enables the Longship.
        private void OnLaunch() { launchedAt = Time.time; launched = true; }
        private void Update()
        {
            if (!ship || SpectatorSession.Active || !Roles.IsDefender || Time.timeScale <= 0f) return;
            // Once launched the ship is leaving: never "complete" its landing again.
            if (launched)
            {
                if (!launchRecovered && !ship.enabled && Time.time - launchedAt >= 4f)
                {
                    // Like the landing event below: without it the fleeing squad never leaves the island.
                    launchRecovered = true;
                    ModLog.Warn("Custom ship launched but the launch event did not fire; sailing off");
                    ship.LaunchAnimComplete();
                }
                return;
            }
            if (ship.interpolator < 1f) return;
            if (arrivedAt < 0f) arrivedAt = Time.time;
            if (!ship.landed && Time.time - arrivedAt >= 5f)
            {
                // Pirate refuses to leave until the landing clip's event marks
                // the ship landed. Recover only after it has reached the shore.
                ModLog.Warn("Custom ship reached shore but landing event did not fire; completing landing");
                ship.LandAnimComplete();
            }
            if (!ship.landed) return;
            if (landedAt < 0f) landedAt = Time.time;
            if (reported || Time.time - landedAt < 6f) return;
            reported = true;
            if (ship.agents.Count == 0) return;
            var agent = ship.agents[0];
            if (!agent) return;
            var updater = Singleton<AgentUpdater>.instance;
            ModLog.Warn("Crew still aboard: updater=" + (updater && updater.isActiveAndEnabled) +
                " spawned=" + agent.spawned.active + " grounded=" + agent.groundedState.active +
                " brain=" + agent.brain.brainState.active + " order=" + agent.brain.order +
                " orderDist=" + agent.orderDist + " orderDir=" + agent.orderDir +
                " speed=" + agent.speed + " hopping=" + agent.body.hopping.active +
                " navValid=" + agent.navPos.valid + " onIsland=" + (bool)agent.navPos.island);
        }
    }
}
