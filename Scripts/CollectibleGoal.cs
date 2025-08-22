using UnityEngine;
using Unity.MLAgents;

/// <summary>
/// Collectible con RECOMPENSA FIJA cuya ALTURA representa el TIEMPO DE VIDA restante.
/// - Al (re)spawn: lifeFraction = 1 (100% de vida) y altura máxima.
/// - Vida aleatoria entre minLifetime y maxLifetime.
/// - lifeFraction decrece linealmente 1 -> 0 respecto a su PROPIA duración.
/// - La ALTURA ahora depende del TIEMPO ABSOLUTO restante (remainingSeconds / maxLifetime). Así un collectible que spawnea con 30s será la mitad de alto que uno con 60s.
/// - remainingSeconds expone (solo lectura) los segundos que faltan antes de la expiración.
/// - Cuando lifeFraction llega a 0 (remainingSeconds ≈ 0) desaparece sin dar recompensa y NOTIFICA al manager.
/// - Si se recoge antes: otorga rewardAmount y NOTIFICA al manager.
/// - El manager (CompetitiveArea) ahora decide CUÁNDO respawnear (no hay respawn automático interno).
/// - "maxRandomValue" queda por compatibilidad histórica (no afecta ya ni a escala ni a recompensa).
/// </summary>
public class CollectibleGoal : MonoBehaviour
{
    [Header("Vida Visual")]
    [UnityEngine.Serialization.FormerlySerializedAs("value")]
    [Tooltip("Fracción de vida restante (derivada: remainingSeconds / m_LifeDuration) - solo lectura")] [SerializeField, Range(0f,1f)] private float lifeFraction = 1f;
    [SerializeField, Tooltip("Segundos restantes antes de expirar (solo lectura)")] private float remainingSeconds = 0f;
    // Parámetros internos (no expuestos) para altura visual ABSOLUTA (en unidades locales) al 100% y al 0% de vida.
    // Se normaliza respecto a la escala original en Y (aunque sea 20) para que la altura visible sea consistente.
    private float m_MaxVisualHeight = 3f;   // altura deseada al 100% de vida
    private float m_MinVisualHeight = 0.1f; // altura deseada justo antes de expirar
    [Tooltip("ID del agente que puede recogerlo. 0 = cualquier agente.")] public int allowedAgentId = 0;
    // Campos obsoletos retirados (decayDuration / maxRandomValue)

    [Header("Vida (s)")]
    [Tooltip("Tiempo de vida mínimo hasta desaparecer si no se recoge")] public float minLifetime = 20f;
    [Tooltip("Tiempo de vida máximo hasta desaparecer si no se recoge")] public float maxLifetime = 60f;

    [Header("Recompensa")] [Tooltip("Recompensa fija otorgada al recoger")] public float rewardAmount = 1f;

    [Header("Visual")] public bool autoColorById = true;
    public Renderer targetRenderer;
    [Tooltip("Color para allowedAgentId = 0 (libre)")] public Color freeColor = Color.white;
    // visualRoot eliminado: se escala directamente el objeto raíz.

    [Tooltip("Manager del área (si se deja null se busca en padres / escena)")] public CompetitiveArea manager;

    private Collider m_Collider;
    private bool m_Collected;
    private Vector3 m_OriginalLocalScale;
    private float m_GroundY; // base en mundo (si pivot centrado: position.y - height/2)
    private float m_SpawnTime;
    private float m_LifeDuration; // duración escogida esta vida
    private bool m_Expired;

    // --- Accesores públicos de solo lectura requeridos para observaciones ---
    /// <summary>
    /// Segundos restantes antes de expirar (0 si ya expiró o fue recogido).
    /// </summary>
    public float RemainingSeconds => remainingSeconds;
    /// <summary>
    /// Fracción de vida restante (0..1) relativa a la duración concreta de esta vida.
    /// </summary>
    public float LifeFraction => lifeFraction;
    /// <summary>
    /// Indica si el collectible ya expiró (true) sin ser recogido.
    /// </summary>
    public bool IsExpired => m_Expired;
    /// <summary>
    /// Indica si está actualmente activo en la escena (proxy de gameObject.activeSelf).
    /// </summary>
    public bool IsActive => gameObject.activeSelf;

    void Awake()
    {
        // Intentar collider en el mismo objeto; si no existe, buscar en hijos (estructura Collectible -> Capsule)
        m_Collider = GetComponent<Collider>();
        if (m_Collider == null)
        {
            m_Collider = GetComponentInChildren<Collider>();
        }
        if (m_Collider && !m_Collider.isTrigger)
        {
            m_Collider.isTrigger = true; // necesitamos trigger para OnTriggerEnter
        }
    // Renderer auto si no se asignó directamente
        if (targetRenderer == null)
        {
            if (targetRenderer == null) targetRenderer = GetComponentInChildren<Renderer>();
        }
    }

    void Start()
    {
        if (manager == null)
        {
            manager = GetComponentInParent<CompetitiveArea>();
#if UNITY_2023_1_OR_NEWER
            if (manager == null) manager = Object.FindFirstObjectByType<CompetitiveArea>();
#else
            if (manager == null) manager = Object.FindObjectOfType<CompetitiveArea>();
#endif
        }
        manager?.RegisterCollectible(this);
    // Cache de escala y base
    m_OriginalLocalScale = transform.localScale;
    // Asumimos pivot centrado verticalmente: base = centerY - height/2
    m_GroundY = transform.position.y - m_OriginalLocalScale.y * 0.5f;
    ResetLife(initial:true);
    ApplyColor();
    }

    void Update()
    {
        if (m_Collected || m_Expired) return;
        if (m_LifeDuration <= 0f) return;

    UpdateLife();
    if (lifeFraction <= 0f) Expire();
    }

    private void Expire()
    {
        if (m_Expired || m_Collected) return;
        m_Expired = true;
        gameObject.SetActive(false);
        manager?.NotifyCollected(this);
        if (manager != null)
        {
            EventCsvLogger.LogExpiration(manager, this);
        }
    }

    private float gracePeriod = 0.5f;

    private bool PreventNegativeSurprise() => Time.time - m_SpawnTime < gracePeriod && rewardAmount < 0f;

    void OnTriggerEnter(Collider other)
    {
        if (PreventNegativeSurprise()) return; // dar un momento para reaccionar a la aparición de rewardAmount negativo
        if (m_Collected) return;
        var agent = other.GetComponentInParent<Agent>();
        if (agent == null) return;
        var compAgent = agent as CompetitiveAgent;
        if (compAgent == null) compAgent = agent.GetComponent<CompetitiveAgent>();

        if (allowedAgentId == 0 || (compAgent != null && compAgent.agentId == allowedAgentId))
        {
            float indivScale = 1f;
            float groupScale = 0f;
            object grs = null;
            System.Type grsType = System.Type.GetType("GlobalRewardSettings");
            if (grsType != null)
            {
                var instProp = grsType.GetProperty("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (instProp != null) grs = instProp.GetValue(null);
                if (grs != null)
                {
                    var indivField = grsType.GetField("individualRewardScale");
                    var groupField = grsType.GetField("groupRewardScale");
                    if (indivField != null) indivScale = Mathf.Max(0f, (float)indivField.GetValue(grs));
                    if (groupField != null) groupScale = Mathf.Max(0f, (float)groupField.GetValue(grs));
                }
            }

            // Recompensa individual escalada
            if (indivScale > 0f)
            {
                agent.AddReward(rewardAmount * indivScale);
            }

            // Recompensa grupal (si hay grupo y escala > 0)
            bool allowGroup = false;
            if (grsType != null && grs != null)
            {
                var enableField = grsType.GetField("enableGroupRewards");
                if (enableField != null)
                {
                    allowGroup = (bool)enableField.GetValue(grs);
                }
            }
            if (groupScale > 0f && allowGroup && manager != null && manager.Group != null)
            {
                manager.Group.AddGroupReward(rewardAmount * groupScale);
            }

            // Log reward (usamos la parte individual para logging del agente específico)
            if (manager != null && compAgent != null)
            {
                EventCsvLogger.LogReward(manager, compAgent, this, rewardAmount * indivScale, compAgent.GetCumulativeReward());
            }
            m_Collected = true;
            m_Expired = false;
            gameObject.SetActive(false);
            manager?.NotifyCollected(this); // manager decide el momento del nuevo spawn
        }
    }

    public void ReactivateAt(Vector3 position)
    {
        // Reinicio de vida (escala completa)
        if (m_OriginalLocalScale == Vector3.zero) m_OriginalLocalScale = transform.localScale == Vector3.zero ? Vector3.one : transform.localScale;
        m_GroundY = position.y;
        transform.position = new Vector3(position.x, position.y + m_OriginalLocalScale.y * 0.5f, position.z);
        ResetLife(initial:false);
        gameObject.SetActive(true);
        ApplyColor();
    }

    private void ResetLife(bool initial)
    {
        m_SpawnTime = Time.time;
        PickLifeDuration();
        remainingSeconds = m_LifeDuration;
        lifeFraction = 1f;
        m_Collected = false;
        m_Expired = false;
        if (initial)
        {
            if (m_OriginalLocalScale == Vector3.zero) m_OriginalLocalScale = transform.localScale == Vector3.zero ? Vector3.one : transform.localScale;
            m_GroundY = transform.position.y - m_OriginalLocalScale.y * 0.5f;
        }
        ApplyScaling(force:true);
    }

    private void UpdateLife()
    {
        if (m_LifeDuration <= 0f) return;
        remainingSeconds = Mathf.Max(0f, m_SpawnTime + m_LifeDuration - Time.time);
        float prevFraction = lifeFraction;
        float newFraction = (m_LifeDuration > 0f) ? remainingSeconds / m_LifeDuration : 0f;
        // Asignar siempre (sin umbral) para garantizar que pueda llegar exactamente a 0
        lifeFraction = newFraction;
        // Si cambió perceptiblemente o la altura depende de remainingSeconds absoluto, aplicar escalado
        if (Mathf.Abs(newFraction - prevFraction) > 0.00005f || newFraction == 0f)
        {
            ApplyScaling(force:false);
        }
    }

    private float m_LastAppliedHeight = -999f;
    private void ApplyScaling(bool force)
    {
        float normRemaining = (maxLifetime > 0f) ? Mathf.Clamp01(remainingSeconds / maxLifetime) : 0f;
        float targetHeight = Mathf.Lerp(m_MinVisualHeight, m_MaxVisualHeight, normRemaining);
        if (!force && Mathf.Abs(targetHeight - m_LastAppliedHeight) < 0.0001f) return;
        m_LastAppliedHeight = targetHeight;
        if (m_OriginalLocalScale == Vector3.zero) m_OriginalLocalScale = transform.localScale == Vector3.zero ? Vector3.one : transform.localScale;
        Vector3 scale = m_OriginalLocalScale; scale.y = targetHeight; transform.localScale = scale;
        float centerY = m_GroundY + targetHeight * 0.5f;
        transform.position = new Vector3(transform.position.x, centerY, transform.position.z);
    }

    private void ApplyColor()
    {
        if (!autoColorById) return;
        if (targetRenderer == null) targetRenderer = GetComponentInChildren<Renderer>();
        if (targetRenderer == null) return;
    var color = (allowedAgentId == 0) ? freeColor : AgentColorUtil.ColorFromId(allowedAgentId);
        var mpb = new MaterialPropertyBlock();
        targetRenderer.GetPropertyBlock(mpb);
        if (mpb == null) mpb = new MaterialPropertyBlock();
        if (targetRenderer.sharedMaterial != null && targetRenderer.sharedMaterial.HasProperty("_Color"))
            mpb.SetColor("_Color", color);
        else
            mpb.SetColor("_BaseColor", color);
        targetRenderer.SetPropertyBlock(mpb);
    }

    private void PickLifeDuration()
    {
        if (maxLifetime < minLifetime) maxLifetime = minLifetime;
        m_LifeDuration = Random.Range(minLifetime, maxLifetime);
    }

    // Método de color reemplazado por AgentColorUtil

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (Application.isPlaying) return;
        if (m_OriginalLocalScale == Vector3.zero) m_OriginalLocalScale = transform.localScale == Vector3.zero ? Vector3.one : transform.localScale;
        m_GroundY = transform.position.y - m_OriginalLocalScale.y * 0.5f;
        PickLifeDuration();
        remainingSeconds = m_LifeDuration;
        lifeFraction = 1f;
        ApplyScaling(force:true);
        ApplyColor();
    }
#endif
}
