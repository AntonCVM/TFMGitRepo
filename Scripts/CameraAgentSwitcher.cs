using UnityEngine;

/// <summary>
/// Control de cámara para alternar entre seguir agentes concretos (teclas 1..N),
/// vista general del área (tecla 0) y ciclar distintos "planos" (Tab).
/// - Asigna manualmente los agentes y (opcional) puntos de overview en el inspector.
/// - Las configuraciones de vista para seguimiento de agente permiten diferentes ángulos / offsets.
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraAgentSwitcher : MonoBehaviour
{
    [Header("Agentes a seguir (orden coincide con teclas 1..N)")]
    public CompetitiveAgent[] agents;

    [Header("Configuraciones de vista al seguir agente")]
    public ViewConfig[] agentViewConfigs = new []
    {
        // 0: Vista tercera persona – valores según captura (Y=30, Z=-5, ángulo X=60)
        new ViewConfig
        {
            positionOffset = new Vector3(0,30f,-5f),
            eulerAnglesOffset = new Vector3(60,0,0),
            worldSpace = false
        },
        // 1: Vista vertical (cenital) – valores según captura (Y=100)
        new ViewConfig
        {
            positionOffset = new Vector3(0,100f,0),
            eulerAnglesOffset = new Vector3(90,0,0),
            worldSpace = true
        },
        // 2: Vista primera persona – valores según captura
        new ViewConfig
        {
            positionOffset = new Vector3(0,1.2f,0),
            eulerAnglesOffset = Vector3.zero,
            worldSpace = false
        }
    };

    [Header("Puntos de overview (usados al pulsar º)")]
    [Tooltip("Lista de transform que definen posiciones y rotaciones fijas para ver todo el área.")]
    public Transform[] overviewPoints;

    [Header("Ajustes")]
    [Tooltip("Velocidad de interpolación de posición")] public float positionLerp = 10f;
    [Tooltip("Velocidad de interpolación de rotación")] public float rotationLerp = 10f;
    [Tooltip("Bloquear cursor (modo juego)")] public bool lockCursor = false;
    [Tooltip("Usar overview cenital calculada en vez de puntos predefinidos")] public bool useTopDownOverview = true;
    [Tooltip("Altura Y para overview cenital (tecla 0)")] public float overviewHeight = 125f;
    [Header("Overview Estática")]
    [Tooltip("Si está activo, la vista 0 no se mueve ni sigue a los agentes")] public bool overviewStatic = true;
    [Tooltip("Ancla opcional para la vista estática; si no se asigna se usa (0,overviewHeight,0) y rotación (90,0,0)")] public Transform overviewStaticAnchor;

    [Header("Debug")] public bool logSwitches = false;

    [System.Serializable]
    public struct ViewConfig
    {
        [Tooltip("Offset de la cámara respecto al agente (o en mundo si worldSpace=true)")] public Vector3 positionOffset;
        [Tooltip("Rotación adicional (Euler). Si worldSpace=true se usa directamente; si no, se suma a la del agente.")] public Vector3 eulerAnglesOffset;
        [Tooltip("Si true, el offset es en coordenadas de mundo y no rota con el agente (útil vista cenital).")]
        public bool worldSpace;
    }

    private int m_CurrentAgentIndex = -1; // -1 = overview
    private int m_AgentViewIndex = 0;     // índice de vista cuando seguimos agente
    private int m_OverviewViewIndex = 0;  // índice de vista cuando estamos en overview (si se usan puntos)
    // (Eliminado suavizado adicional: se usa solo interpolación exponencial estándar)

    private enum ViewMode { Overview, Follow }
    private ViewMode CurrentMode => IsFollowingAgent() ? ViewMode.Follow : ViewMode.Overview;

    void Start()
    {
        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void Update()
    {
        HandleNumberKeys();
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            CycleView();
            if (m_CurrentAgentIndex== -1) m_CurrentAgentIndex = 0;
        }
    }

    private void HandleNumberKeys()
    {
        // º = overview
        if (Input.GetKeyDown(KeyCode.BackQuote))
        {
            SetOverviewMode();
            return;
        }
        // 1..N (hasta 9) limitado por número real de agentes
        int agentCount = (agents != null) ? Mathf.Min(agents.Length, 9) : 0;
        for (int i = 1; i <= agentCount; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha0 + i))
            {
                FollowAgentIndex(i - 1); // array index
                return;
            }
        }
    }

    private void CycleView()
    {
        if (IsFollowingAgent())
        {
            if (agentViewConfigs == null || agentViewConfigs.Length == 0) return;
            m_AgentViewIndex = (m_AgentViewIndex + 1) % agentViewConfigs.Length;
            if (logSwitches) Debug.Log($"[CameraAgentSwitcher] Cambiado a vista de agente {m_CurrentAgentIndex+1}, view {m_AgentViewIndex}");
        }
        else
        {
            if (useTopDownOverview || overviewPoints == null || overviewPoints.Length == 0) return; // top-down única
            m_OverviewViewIndex = (m_OverviewViewIndex + 1) % overviewPoints.Length;
            if (logSwitches) Debug.Log($"[CameraAgentSwitcher] Cambiado a overview point {m_OverviewViewIndex}");
        }
    }

    private void SetOverviewMode()
    {
        // Guardamos el índice de vista de agente (ya está en m_AgentViewIndex) y cambiamos a overview.
        m_CurrentAgentIndex = -1;
        // No reseteamos m_OverviewViewIndex para conservar último plano overview.
        if (logSwitches) Debug.Log($"[CameraAgentSwitcher] Modo overview (vista {m_OverviewViewIndex})");
    }

    private void FollowAgentIndex(int idx)
    {
        if (agents == null || idx < 0 || idx >= agents.Length || agents[idx] == null)
        {
            if (logSwitches) Debug.LogWarning("[CameraAgentSwitcher] Índice de agente inválido");
            return;
        }
        // Mantener m_AgentViewIndex (si nunca se cambió será 0). No se resetea al venir de overview.
        if (agentViewConfigs != null && agentViewConfigs.Length > 0)
        {
            if (m_AgentViewIndex >= agentViewConfigs.Length) m_AgentViewIndex = 0; // clamp/wrap
        }
        else
        {
            m_AgentViewIndex = 0;
        }
        m_CurrentAgentIndex = idx;
        if (logSwitches) Debug.Log($"[CameraAgentSwitcher] Siguiendo agente {idx+1} (vista {m_AgentViewIndex})");
    }

    private bool IsFollowingAgent() => m_CurrentAgentIndex >= 0;

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        // Precalcular factores de interpolación exponencial (estables con dt variable)
        float posAlpha = ExpLerpFactor(positionLerp, dt);
        float rotAlpha = ExpLerpFactor(rotationLerp, dt);

        if (CurrentMode == ViewMode.Follow)
            UpdateFollowAgent(posAlpha, rotAlpha);
        else
            UpdateOverview(posAlpha, rotAlpha);
    }

    private void UpdateFollowAgent(float posAlpha, float rotAlpha)
    {
        if (agents == null || m_CurrentAgentIndex >= agents.Length) return;
        var ag = agents[m_CurrentAgentIndex];
        if (ag == null) return;

        // Determinar configuración
        ViewConfig vc = (agentViewConfigs != null && agentViewConfigs.Length > 0)
            ? agentViewConfigs[Mathf.Clamp(m_AgentViewIndex, 0, agentViewConfigs.Length - 1)]
            : new ViewConfig { positionOffset = new Vector3(0, 2, -5), eulerAnglesOffset = Vector3.zero };

    // Posición objetivo: offset en espacio del agente (o mundo)
    Vector3 desiredPos = vc.worldSpace ? ag.transform.position + vc.positionOffset : ag.transform.TransformPoint(vc.positionOffset);
        transform.position = Vector3.Lerp(transform.position, desiredPos, posAlpha);

        // Rotación
        Quaternion desiredRot = vc.worldSpace ? Quaternion.Euler(vc.eulerAnglesOffset) : ag.transform.rotation * Quaternion.Euler(vc.eulerAnglesOffset);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, rotAlpha);
    }

    private void UpdateOverview(float posAlpha, float rotAlpha)
    {
        if (overviewStatic)
        {
            ApplyStaticOverview();
            return;
        }

        if (!useTopDownOverview && overviewPoints != null && overviewPoints.Length > 0)
        {
            if (ApplyOverviewPoint(posAlpha, rotAlpha)) return;
        }

        ApplyTopDownOverview(posAlpha, rotAlpha);
    }

    private void ApplyStaticOverview()
    {
        if (overviewStaticAnchor != null)
        {
            transform.position = overviewStaticAnchor.position;
            transform.rotation = overviewStaticAnchor.rotation;
        }
        else
        {
            transform.position = new Vector3(0f, overviewHeight, 0f);
            transform.rotation = Quaternion.Euler(90, 0, 0);
        }
    }

    private bool ApplyOverviewPoint(float posAlpha, float rotAlpha)
    {
        if (overviewPoints == null || overviewPoints.Length == 0) return false;
        if (m_OverviewViewIndex >= overviewPoints.Length) m_OverviewViewIndex = 0;
        var p = overviewPoints[Mathf.Clamp(m_OverviewViewIndex, 0, overviewPoints.Length - 1)];
        if (p == null) return false;
        transform.position = Vector3.Lerp(transform.position, p.position, posAlpha);
        transform.rotation = Quaternion.Slerp(transform.rotation, p.rotation, rotAlpha);
        return true;
    }

    private void ApplyTopDownOverview(float posAlpha, float rotAlpha)
    {
        Vector3 center = ComputeAgentsCenter();
        Vector3 desiredPosTop = new Vector3(center.x, overviewHeight, center.z);
        Quaternion desiredRotTop = Quaternion.Euler(90, 0, 0);
        transform.position = Vector3.Lerp(transform.position, desiredPosTop, posAlpha);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotTop, rotAlpha);
    }

    private Vector3 ComputeAgentsCenter()
    {
        if (agents == null || agents.Length == 0) return Vector3.zero;
        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (var a in agents)
        {
            if (a == null) continue;
            sum += a.transform.position;
            count++;
        }
        return count > 0 ? sum / count : Vector3.zero;
    }

    private static float ExpLerpFactor(float speed, float dt)
    {
        if (speed <= 0f) return 1f; // salto inmediato
        return 1f - Mathf.Exp(-speed * dt);
    }
}
