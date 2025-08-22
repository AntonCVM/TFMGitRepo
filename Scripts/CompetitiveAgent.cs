using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public class CompetitiveAgent : Agent
{
    // Rigidbody del agente
    private Rigidbody m_AgentRb;

    [Header("Identificación")]
    [Tooltip("ID único del agente. Si es 0 al iniciar, se asignará automáticamente comenzando en 1.")]
    public int agentId = 0;
    private static int s_NextId = 1;

    [Header("Visual")]
    [Tooltip("Renderer principal del agente (si se deja vacío se busca en hijos).")]
    public Renderer agentRenderer;
    [Tooltip("Aplicar color automáticamente basado en agentId")] public bool autoColorById = true;
    [Tooltip("Color usado si el id <= 0")] public Color neutralColor = Color.white;

    [Header("Observaciones")]
    public bool useVectorObs = true;
    [Tooltip("Último número total de floats observados (solo lectura)")]
    public int lastObservationSize = 0; // actualizado en CollectObservations
    // Contador interno temporal durante CollectObservations
    private int _obsCount = 0;

    [Header("Movimiento")]
    [Tooltip("Fuerza aplicada para avanzar / retroceder (VelocityChange)")]
    public float moveForce = 2f;
    [Tooltip("Ángulo de cada giro discreto en grados")]
    public float rotationStepDegrees = 45f;
    [Tooltip("Tiempo mínimo entre giros discretos (segundos)")]
    public float rotationCooldown = 0.1f;

    // Próximo instante (Time.time) a partir del cual se permite un nuevo giro discreto
    protected float m_NextAllowedRotateTime = 0f;
    private static MaterialPropertyBlock s_Mpb; // cache global para reducir allocs en ApplyColor

    [Header("Recompensas/Penalizaciones")]
    [Tooltip("Penalización negativa fija aplicada cada decisión (0 = sin penalización continua)")]
    public float stepTimePenalty = 0f;
    // Nota: CompetitiveArea la sobrescribe automáticamente a -1/maxEnvironmentSteps en cada ResetEnvironment si maxEnvironmentSteps > 0.

    public override void Initialize()
    {
        m_AgentRb = GetComponent<Rigidbody>();
        if (agentId <= 0)
        {
            agentId = s_NextId++;
        }
    ApplyColor();
    }

    protected override void Awake()
    {
        // Cache early to avoid race conditions where OnEpisodeBegin runs before Initialize
        if (m_AgentRb == null) m_AgentRb = GetComponent<Rigidbody>();
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        if (!useVectorObs) return;
        _obsCount = 0; // reiniciar contador

        PerformObservations(sensor);

        lastObservationSize = _obsCount;
    }

    protected virtual void PerformObservations(VectorSensor sensor)
    {
        var area = GetComponentInParent<CompetitiveArea>();
        Vector3 areaCenter = Vector3.zero;
        Vector3 halfExtents = new Vector3(1f, 1f, 1f);
        if (area != null) area.GetAreaBounds(out areaCenter, out halfExtents);

        // Submétodos para mantener CollectObservations limpio
        AddOwnObservations(sensor, areaCenter, halfExtents);
        AddNearestAgentObservation(sensor, area, areaCenter, halfExtents);
        AddCollectibleObservations(sensor, area, areaCenter, halfExtents);
    }

    // Añade observaciones propias del agente
    protected virtual void AddOwnObservations(VectorSensor sensor, Vector3 areaCenter, Vector3 halfExtents)
    {
        // cooldown normalizado (0..1)
        float cdRem = 0f;
        if (rotationCooldown > 0f) cdRem = Mathf.Clamp01((m_NextAllowedRotateTime - Time.time) / rotationCooldown);
        AddObs(sensor, cdRem);

        // posición relativa normalizada X,Z
        Vector3 selfPos = transform.position - areaCenter;
        float selfNormX = Mathf.Clamp(selfPos.x / (halfExtents.x + 1e-6f), -1f, 1f);
        float selfNormZ = Mathf.Clamp(selfPos.z / (halfExtents.z + 1e-6f), -1f, 1f);
        AddObs(sensor, selfNormX);
        AddObs(sensor, selfNormZ);

        // velocidad en mundo X,Z (normalizada por 10)
        Vector3 myVel = Vector3.zero;
        if (m_AgentRb == null) m_AgentRb = GetComponent<Rigidbody>();
        if (m_AgentRb != null) myVel = m_AgentRb.linearVelocity;
        AddObs(sensor, Mathf.Clamp(myVel.x / 10f, -1f, 1f));
        AddObs(sensor, Mathf.Clamp(myVel.z / 10f, -1f, 1f));

        // orientación plana discretizada a 8 sectores, normalizada 0..1
        float myY = transform.eulerAngles.y;
        int mySector = Mathf.RoundToInt(Mathf.Repeat(myY / 45f, 8f));
        if (mySector == 8) mySector = 0;
        float myRotNorm = mySector / 7f;
        AddObs(sensor, myRotNorm);
    }

    // Añade observación del agente activo más cercano (posX,posZ). Si no hay, pasar 1f,1f
    private void AddNearestAgentObservation(VectorSensor sensor, CompetitiveArea area, Vector3 areaCenter, Vector3 halfExtents)
    {
        if (area == null)
        {
            AddObs(sensor, 1f);
            AddObs(sensor, 1f);
            return;
        }
        var agentsList = area.GetAgents();
    if (agentsList == null || agentsList.Count == 0)
        {
            AddObs(sensor, 1f);
            AddObs(sensor, 1f);
            return;
        }
    CompetitiveAgent nearest = null;
    float bestSqr = float.MaxValue;
    for (int i = 0; i < agentsList.Count; i++)
        {
            var ag = agentsList[i];
            if (ag == null) continue;
            if (ag == this) continue;
            if (!ag.gameObject.activeSelf) continue; // sólo activos
            float sqr = (ag.transform.position - transform.position).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                nearest = ag;
            }
        }
        if (nearest == null)
        {
            AddObs(sensor, 1f);
            AddObs(sensor, 1f);
            return;
        }
        Vector3 rel = nearest.transform.position - areaCenter;
        float normX = Mathf.Clamp(rel.x / (halfExtents.x + 1e-6f), -1f, 1f);
        float normZ = Mathf.Clamp(rel.z / (halfExtents.z + 1e-6f), -1f, 1f);
        AddObs(sensor, normX);
        AddObs(sensor, normZ);
    }

    // Añade observaciones de collectibles pickeables
    private void AddCollectibleObservations(VectorSensor sensor, CompetitiveArea area, Vector3 areaCenter, Vector3 halfExtents)
    {
        if (area == null) return;
        var colList = area.GetAllCollectibles();
        if (colList == null) return;
        for (int i = 0; i < colList.Count; i++)
        {
            var c = colList[i];
            if (c == null)
            {
                // espacio reservado: posX, posZ, remainingNorm
                AddObs(sensor, 0f);
                AddObs(sensor, 0f);
                AddObs(sensor, 0f);
                continue;
            }

            bool canPick = (c.allowedAgentId == 0) || (c.allowedAgentId == agentId);
            if (!canPick) continue; // OMITIR no pickeables

            Vector3 pos = c.transform.position - areaCenter;
            float normX = Mathf.Clamp(pos.x / (halfExtents.x + 1e-6f), -1f, 1f);
            float normZ = Mathf.Clamp(pos.z / (halfExtents.z + 1e-6f), -1f, 1f);
            float remainingNorm = 0f;
            if (c.IsActive && c.maxLifetime > 0f)
            {
                remainingNorm = Mathf.Clamp01(c.RemainingSeconds / c.maxLifetime);
            }
            AddObs(sensor, normX);
            AddObs(sensor, normZ);
            AddObs(sensor, remainingNorm);
        }
    }

    // Helper para centralizar conteo
    protected void AddObs(VectorSensor sensor, float v)
    {
        sensor.AddObservation(v);
        _obsCount++;
    }

    public void MoveAgent(ActionSegment<int> act)
    {
        // Rama discreta 0: 0 = noop, 1 = adelante, 2 = atrás, 3 = girar derecha, 4 = girar izquierda
        var action = act[0];

        if (action == 1)
        {
            if (m_AgentRb != null) m_AgentRb.AddForce(transform.forward * moveForce, ForceMode.VelocityChange);
        }
        else if (action == 2)
        {
            if (m_AgentRb != null) m_AgentRb.AddForce(transform.forward * -moveForce * 0.5f, ForceMode.VelocityChange);
        }
        else if (action == 3 || action == 4)
        {
            // Ejecutar un giro discreto sólo si ha pasado el cooldown
            if (Time.time >= m_NextAllowedRotateTime)
            {
                float dir = (action == 3) ? 1f : -1f; // derecha positivo, izquierda negativo
                transform.Rotate(0f, dir * rotationStepDegrees, 0f, Space.World);
                m_NextAllowedRotateTime = Time.time + rotationCooldown;
            }
        }
    }

    public override void OnActionReceived(ActionBuffers actionBuffers)
    {
        if (stepTimePenalty != 0f)
        {
            AddReward(stepTimePenalty); // tip: usar valor negativo para castigo
        }
        MoveAgent(actionBuffers.DiscreteActions);
    // Shaping local (si el componente AgentShaping está presente)
        if (_agentShaping != null)
        {
            var m = _agentShaping.GetType().GetMethod("OnActionStep", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            m?.Invoke(_agentShaping, null);
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var discreteActionsOut = actionsOut.DiscreteActions;
    // IMPORTANTE: Usamos GetKey (no GetKeyDown) porque las decisiones del agente
    // no ocurren exactamente cada frame; con GetKeyDown se perdían pulsaciones y casi nunca giraba.
    // Priorizamos giros sobre movimiento para que el jugador humano pueda orientar primero.
    int action = 0; // prioridad: rotar sobre mover
    if (Input.GetKey(KeyCode.D)) action = 3;          // girar derecha
    else if (Input.GetKey(KeyCode.A)) action = 4;     // girar izquierda
    else if (Input.GetKey(KeyCode.W)) action = 1;     // avanzar
    else if (Input.GetKey(KeyCode.S)) action = 2;     // retroceder
    discreteActionsOut[0] = action;
    }

    public override void OnEpisodeBegin()
    {
    // Reiniciar velocidad y rotación aleatoria leve si se desea.
    if (m_AgentRb == null) m_AgentRb = GetComponent<Rigidbody>();
    if (m_AgentRb != null) m_AgentRb.linearVelocity = Vector3.zero;
        // Se puede dejar la posición como esté colocada en la escena, o reubicar aleatoriamente si se añade lógica futura.
    m_NextAllowedRotateTime = 0f;
        if (autoColorById) ApplyColor();
        // Notificar manager global (maquina de estados) y aplicar activación segun dificultad
        var mgr = RewardSettingsManager.Instance;
        if (mgr != null)
        {
            mgr.NotifyAgentEpisode(this);
            int diff = mgr.GetCurrentDifficulty();
            int maxAllowed = diff == 1 ? 1 : (diff == 2 ? 2 : 4);
            bool shouldBeActive = agentId > 0 && agentId <= maxAllowed;
            if (gameObject.activeSelf != shouldBeActive)
            {
                gameObject.SetActive(shouldBeActive);
                return; // si se desactiva, abortar resto
            }
        }
        if (_agentShaping != null)
        {
            var m2 = _agentShaping.GetType().GetMethod("OnEpisodeBegin", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            m2?.Invoke(_agentShaping, null);
        }
    }

    // Referencia opcional al componente de shaping local (puede estar en otro assembly)
    private Component _agentShaping;
    private void Start()
    {
    // Intentar obtener componente por tipo directo primero
    _agentShaping = GetComponent(System.Type.GetType("AgentShaping"));
    }

    private void ApplyColor()
    {
        if (!autoColorById) return;
        if (agentRenderer == null) agentRenderer = GetComponentInChildren<Renderer>();
        if (agentRenderer == null) return;
    var color = AgentColorUtil.ColorFromId(agentId);
        if (s_Mpb == null) s_Mpb = new MaterialPropertyBlock();
        agentRenderer.GetPropertyBlock(s_Mpb);
        if (agentRenderer.sharedMaterial != null && agentRenderer.sharedMaterial.HasProperty("_Color"))
            s_Mpb.SetColor("_Color", color);
        else
            s_Mpb.SetColor("_BaseColor", color);
        agentRenderer.SetPropertyBlock(s_Mpb);
    }

    // Color centralizado ahora en AgentColorUtil.

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (autoColorById && Application.isPlaying)
        {
            ApplyColor();
        }
    }
#endif
}
