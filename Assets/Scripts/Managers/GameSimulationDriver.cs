using UnityEngine;

[DefaultExecutionOrder(-32000)]
public sealed class GameSimulationDriver : MonoBehaviour
{
    private void Update() => GameSimulationClock.EnforceNetworkTime();
    private void LateUpdate() => GameSimulationClock.EnforceNetworkTime();
    // Physics2D stays in the project's FixedUpdate automatic simulation mode. Never call Simulate here.
    private void FixedUpdate() => GameSimulationClock.FixedStep(this);
}
