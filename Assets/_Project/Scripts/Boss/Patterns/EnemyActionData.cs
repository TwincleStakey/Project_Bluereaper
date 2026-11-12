using UnityEngine;

[CreateAssetMenu(fileName = "EnemyActionData", menuName = "BlueReaper/EnemyActionData")]
public abstract class EnemyActionData : ScriptableObject
{
    public uint[] RequirePhase = null;
    public bool CanForwardTracking = false; //처음 동작을 수행 할 때 플레이어를 향해 회전과 이동을 수행 할지 여부

    [Min(0f)]
    public float TrackingRotationRatio = 1f; //추적시 회전 속도 비율
    [Min(0f)]
    public float TrackingMoveRatio = 1f; //추적시 이동 속도 비율
    [Min(0f)]
    public float TrackingDuration = 0.15f; //추적 시간
}
