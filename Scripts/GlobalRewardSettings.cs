using UnityEngine;

/// <summary>
/// Configuración GLOBAL para ponderar recompensas individuales vs de grupo.
/// Coloca este componente una sola vez en la escena (por ejemplo en un GameObject llamado "RewardSettings").
/// CollectibleGoal y CompetitiveArea consultarán esta instancia para escalar las recompensas.
/// </summary>
public class GlobalRewardSettings : MonoBehaviour
{
    private static GlobalRewardSettings s_Instance;
    public static GlobalRewardSettings Instance
    {
        get
        {
            if (s_Instance == null)
            {
                s_Instance = FindObjectOfType<GlobalRewardSettings>();
            }
            return s_Instance;
        }
    }

    [Header("Activación Global")]
    [Tooltip("Si está activo se crean grupos y se aplican GroupRewards además de las individuales.")] public bool enableGroupRewards = true;

    [Header("Pesos Globales de Recompensa")]
    [Tooltip("Escala aplicada a TODAS las AddReward individuales.")] [Min(0f)] public float individualRewardScale = 1f;
    [Tooltip("Escala aplicada a TODAS las GroupReward (AddGroupReward). Si 0 se desactiva el aporte grupal aunque enableGroupRewards esté activo.")] [Min(0f)] public float groupRewardScale = 1f;

    private void Awake()
    {
        if (s_Instance != null && s_Instance != this)
        {
            Debug.LogWarning("Ya existe una instancia de GlobalRewardSettings. Se reemplaza la referencia estática.");
        }
        s_Instance = this;
    }

    private void OnDestroy()
    {
        if (s_Instance == this) s_Instance = null;
    }
}
