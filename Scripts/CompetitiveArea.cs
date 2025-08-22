using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Events;
using Unity.MLAgents;

/// <summary>
/// Manager del área competitiva que controla los respawns de los CollectibleGoal.
/// Inspirado en PyramidArea pero simplificado para spawns aleatorios.
/// </summary>
public class CompetitiveArea : MonoBehaviour
{
    [Header("Identificación")]
    public int areaId = 0; // ID único del área, usado para identificarla en el sistema

    [Header("Zonas de Spawn (Transforms)")]
    public Transform[] spawnZones; // Asignar 4 (o más) en el inspector

    // Campos obsoletos de respawn automático eliminados.

    [Header("Reset Settings")]
    [Tooltip("Altura Y para spawnear agentes.")]
    public float agentSpawnHeight = 0f;
    [Tooltip("Número de ciclos completos (cycleDuration) que dura un episodio antes de hacer ResetEnvironment. 0 = sin límite.")]
    public int cyclesPerEpisode = 2;

    [Header("Time Cycle")]
    [Tooltip("Duración en segundos de un ciclo temporal (0 = desactivado)")]
    public float cycleDuration = 120f;
    [Tooltip("Reiniciar automáticamente al terminar el ciclo")]
    public bool cycleAutoRestart = true;
    [Tooltip("Número de subciclos lógicos dentro del ciclo principal (>=1)")]
    public int cycleSubdivisions = 4;
    [Tooltip("Progreso normalizado actual del ciclo (0..1) (solo lectura)")]
    [SerializeField] private float cycleProgress = 0f;
    [Tooltip("Tiempo transcurrido dentro del ciclo actual (solo lectura)")]
    [SerializeField] private float cycleElapsed = 0f;
    [Tooltip("Índice de subciclo actual (0..cycleSubdivisions-1) (solo lectura)")]
    [SerializeField] private int currentSubcycleIndex = 0;
    [Tooltip("Progreso normalizado dentro del subciclo actual (0..1) (solo lectura)")]
    [SerializeField] private float subcycleProgress = 0f;
    [Tooltip("Número total de ciclos completos desde que se inició el área (solo lectura)")]
    [SerializeField] private int cyclesCompleted = 0;

    [System.Serializable] public class FloatEvent : UnityEvent<float> { }
    [System.Serializable] public class IntEvent : UnityEvent<int> { }
    [Header("Cycle Events")] public FloatEvent onCycleProgress; // invocado cada frame con progreso 0..1
    public UnityEvent onCycleLoop; // invocado al completar el ciclo
    [Tooltip("Invocado cuando cambia el índice de subciclo (nuevo índice)")] public IntEvent onSubcycleChanged;
    [Tooltip("Invocado cada frame con el progreso dentro del subciclo [0..1]")] public FloatEvent onSubcycleProgress;

    [Header("Agentes (registro manual) ")]
    [Tooltip("Lista de agentes gestionados por esta área. Se registran en Start o vía RegisterAgent.")]
    public CompetitiveAgent[] agents;

    [Header("Multi-Agente (Group Reward)")]
    private SimpleMultiAgentGroup m_Group;
    // Seguimiento para evitar doble registro y excepciones
    private HashSet<Agent> m_GroupAgents = new HashSet<Agent>();
    public SimpleMultiAgentGroup Group => m_Group;

    [Header("Spawn Automático Controlado")]
    [Tooltip("Intervalo en segundos entre intentos de spawn")] public float spawnInterval = 1f;
    [Range(0f, 1f), Tooltip("Probabilidad de que en un intento programado realmente ocurra un spawn")] public float spawnProbability = 0.8f;

    [System.Serializable]
    public class IdProportion
    {
        [Tooltip("ID de agente asociado a collectibles que pueden spawnearse (0 = libres)")] public int allowedAgentId = 0;
        [Tooltip("Peso relativo dentro del subciclo")] public float weight = 1f;
    }

    [System.Serializable]
    public class SubcycleSpawnProportions
    {
        [Tooltip("Lista de pesos por allowedAgentId para este subciclo")] public List<IdProportion> idProportions = new List<IdProportion>();
        [HideInInspector] public float cachedTotalWeight;
        public void RecomputeTotal()
        {
            float total = 0f;
            if (idProportions != null)
            {
                foreach (var p in idProportions)
                {
                    if (p != null && p.weight > 0f) total += p.weight;
                }
            }
            cachedTotalWeight = total;
        }
    }

    [Tooltip("Configuración de proporciones por subciclo. Índice debe corresponder al subciclo (0..cycleSubdivisions-1). Si falta, se reutiliza la última.")]
    public List<SubcycleSpawnProportions> subcycleSpawnConfigs = new List<SubcycleSpawnProportions>();

    // Tiempo acumulado del episodio actual (segundos)
    private float m_EpisodeElapsed;

    private readonly List<CollectibleGoal> m_Collectibles = new List<CollectibleGoal>();
    private readonly List<CollectibleGoal> m_InactiveCollectibles = new List<CollectibleGoal>(); // collectibles recogidos/expirados esperando spawn
    private float m_NextSpawnTime;

    public event System.Action OnResetEnvironment;

    private int _episodeIndex = 0; // starts at 0, first reset increments to 1
    public int GetEpisodeIndex() => _episodeIndex;

    // --- Acceso público para observaciones ---
    public IReadOnlyList<CollectibleGoal> GetAllCollectibles() => m_Collectibles;
    public IReadOnlyList<CollectibleGoal> GetInactiveCollectibles() => m_InactiveCollectibles;
    public IReadOnlyList<CompetitiveAgent> GetAgents() => agents;
    public int GetCurrentSubcycle() => currentSubcycleIndex;

    // Calcula un punto de referencia y un tamaño aproximado para normalizar posiciones dentro del área.
    // Estrategia: si existen spawnZones se toma el AABB (bounding box) que las contiene, y se usan sus extents.
    // Devuelve center y halfExtents (si no hay zonas: halfExtents=(1,1,1)).
    public void GetAreaBounds(out Vector3 center, out Vector3 halfExtents)
    {
        if (spawnZones != null && spawnZones.Length > 0)
        {
            bool started = false;
            Vector3 min = Vector3.zero, max = Vector3.zero;
            foreach (var t in spawnZones)
            {
                if (t == null) continue;
                var p = t.position;
                if (!started)
                {
                    min = max = p; started = true; continue;
                }
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            if (started)
            {
                center = (min + max) * 0.5f;
                var size = (max - min);
                // Evitar cero
                halfExtents = new Vector3(Mathf.Max(0.5f, size.x * 0.5f), Mathf.Max(0.5f, size.y * 0.5f), Mathf.Max(0.5f, size.z * 0.5f));
                return;
            }
        }
        center = transform.position;
        halfExtents = new Vector3(1f, 1f, 1f);
    }

    void Start()
    {
        // Asegurar que cualquier collectible que exista en escena pero no se haya registrado aún lo haga.
        StartCoroutine(InitialSetupNextFrame());
        // Cache de pesos por subciclo
        RecomputeAllWeights();

        // Crear grupo si corresponde
        // Crear grupo según configuración global (reflexión para tolerar orden de compilación)
        if (m_Group == null)
        {
            var grsType = System.Type.GetType("GlobalRewardSettings");
            bool enable = false;
            object grsInst = null;
            if (grsType != null)
            {
                var instProp = grsType.GetProperty("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (instProp != null) grsInst = instProp.GetValue(null);
                if (grsInst != null)
                {
                    var enableField = grsType.GetField("enableGroupRewards");
                    if (enableField != null) enable = (bool)enableField.GetValue(grsInst);
                }
            }
            if (enable)
            {
                m_Group = new SimpleMultiAgentGroup();
                m_GroupAgents.Clear();
                if (agents != null)
                {
                    foreach (var ag in agents)
                    {
                        if (ag == null) continue;
                        // Intentar despegar de grupo previo si existe
                        if (!m_GroupAgents.Contains(ag))
                        {
                            m_Group.RegisterAgent(ag);
                            m_GroupAgents.Add(ag);
                        }
                    }
                }
            }
        }
    }

    private IEnumerator InitialSetupNextFrame()
    {
        // Esperar un frame para permitir que Start() de los collectibles/agents se ejecute y se registren.
        yield return null;
        // Asegurar orden determinista por nombre: ordenar la lista interna de collectibles
        if (m_Collectibles != null && m_Collectibles.Count > 1)
        {
            m_Collectibles.Sort((a, b) =>
            {
                if (a == null && b == null) return 0;
                if (a == null) return 1;
                if (b == null) return -1;
                return string.Compare(a.name, b.name, System.StringComparison.Ordinal);
            });
        }
        ResetEnvironment();
    }

    void Update()
    {
        // Control por número de ciclos (tiempo) en lugar de steps
        if (cycleDuration > 0f && cyclesPerEpisode > 0)
        {
            m_EpisodeElapsed += Time.deltaTime;
            float episodeDurationTarget = cycleDuration * cyclesPerEpisode;
            if (m_EpisodeElapsed >= episodeDurationTarget)
            {
                ResetEnvironment();
            }
        }

        UpdateTimeCycle(); // ahora siempre avanza, independiente de respawns

        HandleAutoSpawns();
    }

    public void RegisterCollectible(CollectibleGoal c)
    {
        if (!m_Collectibles.Contains(c))
        {
            m_Collectibles.Add(c);
        }
    }

    public void NotifyCollected(CollectibleGoal c)
    {
        if (!m_Collectibles.Contains(c)) return; // no registrado
        if (!m_InactiveCollectibles.Contains(c))
        {
            m_InactiveCollectibles.Add(c);
        }
    }

    public void IncrementEpisode()
    {
        _episodeIndex++;
    }

    public void ResetEnvironment()
    {
        m_EpisodeElapsed = 0f;
        cyclesCompleted = 0; // reiniciar contador de ciclos al comienzo del episodio
        // Terminar episodio de grupo (inicia implícitamente uno nuevo en el siguiente uso)
        var grsType3 = System.Type.GetType("GlobalRewardSettings");
        bool endEnable = false;
        if (grsType3 != null)
        {
            var instProp3 = grsType3.GetProperty("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            var inst3 = instProp3?.GetValue(null);
            if (inst3 != null)
            {
                var enableField3 = grsType3.GetField("enableGroupRewards");
                if (enableField3 != null) endEnable = (bool)enableField3.GetValue(inst3);
            }
        }
        if (endEnable && m_Group != null)
        {
            m_Group.EndGroupEpisode();
            // No limpiamos m_GroupAgents porque mantenemos asociación para siguiente episodio.
        }
        // Reposicionar agentes
        if (agents != null)
            foreach (var agent in agents)
            {
                if (agent == null) continue;
                // Finalizar episodio anterior (como ya no usamos MaxStep interno)
                agent.EndEpisode();
                Vector3 pos = SampleRandomPointInRandomZone(agentSpawnHeight);
                agent.transform.position = pos;
                // Rotación en múltiplos de 45°
                int step = Random.Range(0, 8); // 0..7
                float yaw = step * 45f;
                agent.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                var rb = agent.GetComponent<Rigidbody>();
                if (rb)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
            }
        // No spawnear automáticamente: marcar todos inactivos para spawn manual posterior
        m_InactiveCollectibles.Clear();
        foreach (var col in m_Collectibles)
        {
            if (col == null) continue;
            if (col.gameObject.activeSelf)
            {
                col.gameObject.SetActive(false);
            }
            if (!m_InactiveCollectibles.Contains(col))
            {
                m_InactiveCollectibles.Add(col);
            }
        }

        // Reiniciar ciclo temporal también al resetear el entorno
        ResetCycle();
        m_NextSpawnTime = Time.time + spawnInterval;
        IncrementEpisode();
        // Invocar evento de reset
        OnResetEnvironment?.Invoke();
    }

    private Vector3 SampleRandomPointInRandomZone(float heightOffset)
    {
        if (spawnZones == null || spawnZones.Length == 0)
            return new Vector3(0f, heightOffset, 0f);
        var zone = spawnZones[Random.Range(0, spawnZones.Length)];
        if (zone == null) return new Vector3(0f, heightOffset, 0f);
        var scale = zone.localScale;
        float xRange = scale.x / 2.1f;
        float zRange = scale.z / 2.1f;
        var localOffset = new Vector3(Random.Range(-xRange, xRange), 0f, Random.Range(-zRange, zRange));
        float y = zone.position.y + heightOffset;
        return new Vector3(zone.position.x + localOffset.x, y, zone.position.z + localOffset.z);
    }

    private void RespawnCollectible(CollectibleGoal c)
    {
        if (c == null) return;
        var pos = SampleRandomPointInRandomZone(0f);
        c.ReactivateAt(pos);
    }

    // --- API manual de respawn filtrado por allowedAgentId ---
    /// <summary>
    /// Respawnea un collectible inactivo filtrando por allowedAgentId exacto.
    /// - allowedAgentIdFilter > 0: sólo collectibles cuyo allowedAgentId == valor.
    /// - allowedAgentIdFilter == 0: sólo collectibles libres (allowedAgentId == 0).
    /// Devuelve true si se respawneó alguno.
    /// </summary>
    public bool SpawnRandomInactiveByAllowedId(int allowedAgentIdFilter)
    {
        if (m_InactiveCollectibles.Count == 0) return false;
        // Reunir índices candidatos sin crear listas grandes de objetos (evitar LINQ/allocs)
        var candidateIndices = new List<int>();
        for (int i = 0; i < m_InactiveCollectibles.Count; i++)
        {
            var c = m_InactiveCollectibles[i];
            if (c == null) continue;
            if (c.allowedAgentId == allowedAgentIdFilter)
            {
                candidateIndices.Add(i);
            }
        }
        if (candidateIndices.Count == 0) return false;
        int pick = candidateIndices[Random.Range(0, candidateIndices.Count)];
        var chosen = m_InactiveCollectibles[pick];
        m_InactiveCollectibles.RemoveAt(pick);
        RespawnCollectible(chosen);
        return true;
    }

    private void HandleAutoSpawns()
    {
        if (!ShouldAttemptAutoSpawn()) return;
        int allowedId = ChooseAllowedAgentId();
        if (allowedId == int.MinValue) return; // nada elegido
        SpawnRandomInactiveByAllowedId(allowedId);
    }

    private bool ShouldAttemptAutoSpawn()
    {
        if (spawnInterval <= 0f) return false;
        if (Time.time < m_NextSpawnTime) return false;
        // Programar el próximo intento siempre, independientemente de si hay collectibles.
        m_NextSpawnTime = Time.time + spawnInterval;
        // Sólo intentar si hay inactivos y pasa la probabilidad.
        return m_InactiveCollectibles.Count > 0 && Random.value <= spawnProbability;
    }

    private int ChooseAllowedAgentId()
    {
        var config = GetCurrentSubcycleConfig();
        if (config != null && config.cachedTotalWeight > 0f && config.idProportions != null && config.idProportions.Count > 0)
        {
            float r = Random.value * config.cachedTotalWeight;
            float acc = 0f;
            for (int i = 0; i < config.idProportions.Count; i++)
            {
                var p = config.idProportions[i];
                if (p == null || p.weight <= 0f) continue;
                acc += p.weight;
                if (r <= acc) return p.allowedAgentId;
            }
        }
        // Fallback uniforme sobre IDs presentes
        return RandomExistingInactiveAllowedId();
    }

    private SubcycleSpawnProportions GetCurrentSubcycleConfig()
    {
        if (subcycleSpawnConfigs == null || subcycleSpawnConfigs.Count == 0) return null;
        int subIndex = currentSubcycleIndex;
        if (subIndex < subcycleSpawnConfigs.Count) return subcycleSpawnConfigs[subIndex];
        return subcycleSpawnConfigs[subcycleSpawnConfigs.Count - 1];
    }

    private int RandomExistingInactiveAllowedId()
    {
        var unique = new List<int>();
        for (int i = 0; i < m_InactiveCollectibles.Count; i++)
        {
            var c = m_InactiveCollectibles[i];
            if (c == null) continue;
            int id = c.allowedAgentId;
            bool exists = false;
            for (int j = 0; j < unique.Count; j++) if (unique[j] == id) { exists = true; break; }
            if (!exists) unique.Add(id);
        }
        if (unique.Count == 0) return int.MinValue;
        return unique[Random.Range(0, unique.Count)];
    }

    private void RecomputeAllWeights()
    {
        if (subcycleSpawnConfigs == null) return;
        foreach (var cfg in subcycleSpawnConfigs) cfg?.RecomputeTotal();
    }


    private void OnDrawGizmosSelected()
    {
        if (spawnZones == null) return;
        Gizmos.color = Color.cyan;
        foreach (var t in spawnZones)
        {
            if (t == null) continue;
            Gizmos.DrawWireCube(t.position, t.localScale);
        }
    }

    // --- Time Cycle Logic ---
    private void UpdateTimeCycle()
    {
        if (cycleDuration <= 0f) return;
        if (cycleSubdivisions < 1) cycleSubdivisions = 1;

        cycleElapsed += Time.deltaTime;
        float newProgress = Mathf.Clamp01(cycleElapsed / cycleDuration);
        bool progressChanged = !Mathf.Approximately(newProgress, cycleProgress);
        if (progressChanged)
        {
            cycleProgress = newProgress;
            onCycleProgress?.Invoke(cycleProgress);

            // Subciclo
            float scaled = cycleProgress * cycleSubdivisions; // 0..subdivisions
            int newIndex = Mathf.Min(cycleSubdivisions - 1, (int)scaled);
            float newSubProg = scaled - newIndex; // 0..1 dentro subciclo

            if (newIndex != currentSubcycleIndex)
            {
                currentSubcycleIndex = newIndex;
                onSubcycleChanged?.Invoke(currentSubcycleIndex);
            }
            if (!Mathf.Approximately(newSubProg, subcycleProgress))
            {
                subcycleProgress = newSubProg;
                onSubcycleProgress?.Invoke(subcycleProgress);
            }
        }

        if (cycleElapsed >= cycleDuration)
        {
            onCycleLoop?.Invoke();
            cyclesCompleted++;
            if (cycleAutoRestart)
            {
                cycleElapsed = 0f;
                cycleProgress = 0f;
                currentSubcycleIndex = 0;
                subcycleProgress = 0f;
                onCycleProgress?.Invoke(0f);
                onSubcycleChanged?.Invoke(0);
                onSubcycleProgress?.Invoke(0f);
            }
        }
    }

    public float GetCycleProgress() => cycleProgress;
    public float GetCycleElapsed() => cycleElapsed;
    public int GetCurrentSubcycleIndex() => currentSubcycleIndex;
    public float GetSubcycleProgress() => subcycleProgress;
    public int GetCyclesCompleted() => cyclesCompleted;
    public void ResetCycle(float? newDuration = null)
    {
        if (newDuration.HasValue) cycleDuration = Mathf.Max(0f, newDuration.Value);
        cycleElapsed = 0f;
        cycleProgress = 0f;
        currentSubcycleIndex = 0;
        subcycleProgress = 0f;
        if (cycleDuration > 0f) onCycleProgress?.Invoke(0f);
        if (cycleDuration > 0f)
        {
            onSubcycleChanged?.Invoke(0);
            onSubcycleProgress?.Invoke(0f);
        }
    }

    void OnValidate()
    {
        if (cyclesPerEpisode < 0) cyclesPerEpisode = 0;
        if (spawnInterval < 0f) spawnInterval = 0f;
        if (spawnProbability < 0f) spawnProbability = 0f;
        if (spawnProbability > 1f) spawnProbability = 1f;
        RecomputeAllWeights();
    }

    [ContextMenu("ForceResetEnvironment")]
    private void Reset()
    {
        ResetEnvironment();
    }

}
