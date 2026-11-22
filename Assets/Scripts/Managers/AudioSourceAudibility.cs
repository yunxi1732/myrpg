using UnityEngine;

public static class AudioSourceAudibility
{
    public static bool IsAudible(Transform source, float minimumDistance)
    {
        Player player = PlayerManager.instance != null ? PlayerManager.instance.player : null;
        return player == null || Vector2.Distance(player.transform.position, source.position) <= minimumDistance;
    }
}
