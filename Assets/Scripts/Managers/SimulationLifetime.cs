using UnityEngine;

public sealed class SimulationLifetime : MonoBehaviour
{
    private float remaining = float.PositiveInfinity;
    public void Schedule(float delay) => remaining = Mathf.Min(remaining, delay);
    private void Update()
    {
        remaining -= GameSimulationClock.DeltaTime;
        if (remaining <= 0) Destroy(gameObject);
    }
}
