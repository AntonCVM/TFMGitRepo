using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public class CompetitiveAgentD : Agent
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
    private float m_NextAllowedRotateTime = 0f;
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

    public override void CollectObservations(VectorSensor sensor)
    {
        if (!useVectorObs) return;
    _obsCount = 0; // reiniciar contador

    // 1. Datos propios básicos (removida la velocidad local para evitar redundancia con el bloque de agentes)
    float cdRem = 0f;
    if (rotationCooldown > 0f) cdRem = Mathf.Clamp01((m_NextAllowedRotateTime - Time.time) / rotationCooldown);
    AddObs(sensor, cdRem);      // cooldown rotación
    AddObs(sensor, agentId);    // ID propio

        // 2. Datos de todos los agentes del área (incluyéndome)
        // Requerido por el usuario:
        //  - Posición relativa al Area y normalizada (X,Z)
        //  - Velocidad (X,Z)
        //  - Rotación plana discretizada (0..7) -> one-hot o normalizado. Usaremos valor normalizado [0..1] = idx/7.
        //  - ID
        var area = GetComponentInParent<CompetitiveArea>();
    if (area != null)
        {
            area.GetAreaBounds(out var areaCenter, out var halfExtents);
            var agentsList = area.GetAgents();
            if (agentsList != null)
            {
                for (int i = 0; i < agentsList.Count; i++)
                {
                    var ag = agentsList[i];
                    if (ag == null)
                    {
                        // Rellenar con ceros para mantener longitud consistente
            AddObs(sensor, 0f); // posX
            AddObs(sensor, 0f); // posZ
            AddObs(sensor, 0f); // velX
            AddObs(sensor, 0f); // velZ
            AddObs(sensor, 0f); // rotNorm
            AddObs(sensor, 0f); // id
                        continue;
                    }
                    // Posición relativa normalizada
                    Vector3 pos = ag.transform.position - areaCenter;
                    float normX = Mathf.Clamp(pos.x / (halfExtents.x + 1e-6f), -1f, 1f);
                    float normZ = Mathf.Clamp(pos.z / (halfExtents.z + 1e-6f), -1f, 1f);
                    // Velocidad en mundo ignorando Y
                    // Obtener rigidbody del otro agente (no podemos acceder a su campo privado directamente)
                    Vector3 vel = Vector3.zero;
                    var otherRb = ag.GetComponent<Rigidbody>();
                    if (otherRb) vel = otherRb.linearVelocity;
                    // Discretizar rotación Y a múltiplos de 45 grados (0..7)
                    float yAngle = ag.transform.eulerAngles.y;
                    int sector = Mathf.RoundToInt(Mathf.Repeat(yAngle / 45f, 8f));
                    if (sector == 8) sector = 0;
                    float rotNorm = sector / 7f; // 0..1
                    AddObs(sensor, normX);
                    AddObs(sensor, normZ);
                    AddObs(sensor, Mathf.Clamp(vel.x / 10f, -1f, 1f)); // velX
                    AddObs(sensor, Mathf.Clamp(vel.z / 10f, -1f, 1f)); // velZ
                    AddObs(sensor, rotNorm);
                    AddObs(sensor, ag.agentId); // ID
                }
            }

            // 3. Datos de todos los collectibles del área (activos e inactivos)
            var colList = area.GetAllCollectibles();
            if (colList != null)
            {
                for (int i = 0; i < colList.Count; i++)
                {
                    var c = colList[i];
        if (c == null)
                    {
                        // espacio reservado
            AddObs(sensor, 0f); // posX
            AddObs(sensor, 0f); // posZ
            AddObs(sensor, 0f); // allowedId
            AddObs(sensor, 0f); // isActive
            AddObs(sensor, 0f); // remainingSeconds
                        continue;
                    }
                    Vector3 pos = c.transform.position - areaCenter;
                    float normX = Mathf.Clamp(pos.x / (halfExtents.x + 1e-6f), -1f, 1f);
                    float normZ = Mathf.Clamp(pos.z / (halfExtents.z + 1e-6f), -1f, 1f);
                    float allowed = c.allowedAgentId;
                    float active = c.IsActive ? 1f : 0f;
                    // Tiempo restante: opcional normalizar por maxLifetime del propio collectible (siempre definido en c.maxLifetime)
                    float remainingNorm = 0f;
                    if (c.maxLifetime > 0f) remainingNorm = Mathf.Clamp01(c.RemainingSeconds / c.maxLifetime);
                    AddObs(sensor, normX);
                    AddObs(sensor, normZ);
                    AddObs(sensor, allowed);
                    AddObs(sensor, active);
                    AddObs(sensor, remainingNorm);
                }
            }

            // 4. Subciclo actual (como entero). Puede normalizarse por subdivisions-1.
            int subIdx = area.GetCurrentSubcycle();
            int subdiv = Mathf.Max(1, area.cycleSubdivisions);
            float subNorm = (subdiv > 1) ? (float)subIdx / (subdiv - 1) : 0f;
            AddObs(sensor, subIdx);  // entero bruto
            AddObs(sensor, subNorm); // normalizado
        }
        lastObservationSize = _obsCount;
    }

    // Helper para centralizar conteo
    private void AddObs(VectorSensor sensor, float v)
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
            m_AgentRb.AddForce(transform.forward * moveForce, ForceMode.VelocityChange);
        }
        else if (action == 2)
        {
            m_AgentRb.AddForce(transform.forward * -moveForce * 0.5f, ForceMode.VelocityChange);
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
        m_AgentRb.linearVelocity = Vector3.zero;
        // Se puede dejar la posición como esté colocada en la escena, o reubicar aleatoriamente si se añade lógica futura.
    m_NextAllowedRotateTime = 0f;
        if (autoColorById) ApplyColor();
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
