using UnityEngine;
using System; // para StringComparison
using TFM.Navigation;
using Unity.MLAgents.Sensors;
using UnityEngine.UIElements;

/// <summary>
/// Agent que refresca su nodo en el grafo antes de realizar observaciones.
/// </summary>
public class AgentInGraph : CompetitiveAgent
{
    /// <summary>
    /// Busca la primera entrada en signalExpiryTimes cuyo SignalId empieza por el prefijo dado.
    /// </summary>
    private bool TryGetFirstExpiryTimeByPrefix(string prefix, out float expiryTime)
    {
        expiryTime = 0f;
        if (string.IsNullOrEmpty(prefix) || signalExpiryTimes.Count == 0) return false;
        foreach (var kvp in signalExpiryTimes)
        {
            if (kvp.Key != null && kvp.Key.StartsWith(prefix, StringComparison.Ordinal))
            {
                expiryTime = kvp.Value;
                return true;
            }
        }
        return false;
    }
    // Diccionario: SignalId -> fecha de caducidad (float, tiempo absoluto de expiración)
    private System.Collections.Generic.Dictionary<string, float> signalExpiryTimes = new System.Collections.Generic.Dictionary<string, float>();

    private GraphNode _graphNode;

    public float signalImprovementRewardFactor = 0.01f;
    [Tooltip("Cota máxima de 'improvement' considerada en un único tick para evitar picos de reward instantáneo por la propagación de señales en profundidad.")]
    public float maxSignalImprovementPerStep = 5f;


    protected override void Awake()
    {
        base.Awake();
        _graphNode = GetComponent<GraphNode>();
        if (_graphNode != null)
        {
            _graphNode.OnRecordImproved += HandleRecordImprovement;
            _graphNode.OnNewSignalReceived += HandleNewSignalReceived; // Keep the single instance
            _graphNode.OnSignalRemoved += HandleSignalRemoved; // Keep the single instance
        }
    }

    private void Start()
    {
        if (InputManager.Instance != null)
        {
            InputManager.Instance.OnToggleDirectionGizmoKey += ToggleDirectionGizmo;
        }
    }

    protected virtual void OnDestroy()
    {
        if (_graphNode != null)
        {
            _graphNode.OnRecordImproved -= HandleRecordImprovement;
            _graphNode.OnNewSignalReceived -= HandleNewSignalReceived;
            _graphNode.OnSignalRemoved -= HandleSignalRemoved;
        }
        if (InputManager.Instance != null)
        {
            InputManager.Instance.OnToggleDirectionGizmoKey -= ToggleDirectionGizmo;
        }

    }

    protected override void PerformObservations(VectorSensor sensor)
    {
        if (_graphNode != null)
        {
            _graphNode.RefreshNode();
        }
        var area = GetComponentInParent<CompetitiveArea>();
        Vector3 areaCenter = Vector3.zero;
        Vector3 halfExtents = new(1f, 1f, 1f);
        if (area != null) area.GetAreaBounds(out areaCenter, out halfExtents);

        AddOwnObservations(sensor); // llamada a override local

        // Siempre usar broadcasters
        AddBroadcasterSignalsObservations(sensor);
    }

    public override void OnEpisodeBegin()
    {
        if (_graphNode != null)
        {
            _graphNode.ClearSignalsAndRecords();
            _graphNode.RefreshNode();
        }
        base.OnEpisodeBegin();
    }

    private void HandleRecordImprovement(GraphNode node, Signal signal, float improvement)
    {
        // APAÑO / HOTFIX:
        // En propagación profunda pueden llegar al agente dos versiones de la misma señal
        // con un salto grande de distancia (improvement elevado) en un solo frame,
        // generando un reward artificial. Cambio: si el improvement supera la cota, NO se concede reward.
        // NOTA: Futuro -> usar BFS o acumulación temporal para no descartar mejoras legítimas largoplazo.
        if (signalImprovementRewardFactor > 0f && improvement > 0f && improvement <= maxSignalImprovementPerStep)
        {
            AddReward(signalImprovementRewardFactor * improvement);
        }
        // else: improvement <= 0, factor <= 0 o supera la cota -> se ignora.
    }

    // Evento: se recibe una nueva señal
    private void HandleNewSignalReceived(GraphNode node, Signal signal)
    {
        // Usar SignalId como clave
        var originNode = signal.OriginNode;
        if (originNode == null) return;
        var collectible = originNode.GetComponent<CollectibleGoal>();
        if (collectible == null) return;
        float expiry = Time.time + collectible.RemainingSeconds;
        signalExpiryTimes[signal.SignalId] = expiry;
    }

    // Evento: se elimina una señal
    private void HandleSignalRemoved(GraphNode node, Signal signal)
    {
        if (signal == null) return;
        signalExpiryTimes.Remove(signal.SignalId);
    }

    public float maxSpeed = 42f;

    /// <summary>
    /// Observaciones propias compactas:
    /// 0) cooldown de rotación normalizado (0..1)
    /// 1) forward.x (orientación lateral)
    /// 2) forward.z (orientación longitudinal)
    /// </summary>
    /// <param name="sensor">Sensor destino.</param>
    protected void AddOwnObservations(VectorSensor sensor)
    {
        // cooldown normalizado (0..1)
        float cdRem = 0f;
        if (rotationCooldown > 0f) cdRem = Mathf.Clamp01((m_NextAllowedRotateTime - Time.time) / rotationCooldown);
        AddObs(sensor, cdRem);

        Vector3 forward = transform.forward;
        // Observación 1: orientación lateral (derecha +, izquierda -)
        AddObs(sensor, forward.x);
        // Observación 2: orientación longitudinal (adelante +, atrás -)
        AddObs(sensor, forward.z);

        Vector3 velocity = GetComponent<Rigidbody>().linearVelocity;
        float speed = 0f, sin = 0f, cos = 0f;
        (speed, sin, cos) = RelativePolar(velocity, maxSpeed);
        // Observación 3: velocidad relativa
        AddObs(sensor, speed);
        AddObs(sensor, sin);
        AddObs(sensor, cos);
    }

    [Header("Observaciones Señales")]
    [Tooltip("Distancia máxima considerada para normalizar la distancia al último propagador de la señal propia más cercana.")]
    public float maxPolarDistance = 20f;

    [Header("Observaciones Broadcasters Externos")]
    [Tooltip("Lista de BroadcasterNode a observar. Para cada uno se añaden 4 observaciones (distNorm, sin, cos, expiryNorm) relativas a la primera señal cuyo ID comience con su prefijo base. Mantener tamaño estable durante entrenamiento.")]
    public System.Collections.Generic.List<BroadcasterNode> observedBroadcasters = new System.Collections.Generic.List<BroadcasterNode>();
    // Si observedBroadcasters tiene elementos, se añaden observaciones externas.
    // Caché de prefijos (alineada con observedBroadcasters). Cada entrada termina en '.' y NO incluye el sufijo incremental.
    private System.Collections.Generic.List<string> _cachedBroadcasterPrefixes = new System.Collections.Generic.List<string>();

    /// <summary>
    /// Para cada Broadcaster de la lista añade 4 observaciones:
    ///   - distNorm: distancia normalizada al último propagador de la primera señal cuyo ID empieza por el prefijo derivado del broadcaster
    ///   - sin: seno del ángulo relativo
    ///   - cos: coseno del ángulo relativo
    ///   - expiryNorm: tiempo restante normalizado (1 equivale a 60s, 0 equivale a caducado o sin señal)
    /// Prefijo derivado: (collectible.allowedAgentId + ".")? + broadcasterBaseId + "."  (sin el sufijo incremental).
    /// Si no se encuentra señal aún: 0,0,0,0.
    /// IMPORTANTE: Mantener número de broadcasters constante entre episodios para no alterar el tamaño del vector de observaciones.
    /// </summary>
    private void AddBroadcasterSignalsObservations(VectorSensor sensor)
    {
        // Limpiar y preparar lista de polares
        _lastBroadcasterPolars.Clear();
        if (_graphNode == null)
        {
            for (int i = 0; i < observedBroadcasters.Count; i++) {
                AddObs(sensor, 0f); AddObs(sensor, 0f); AddObs(sensor, 0f); AddObs(sensor, 0f);
                _lastBroadcasterPolars.Add((0f, 0f, 0f, 0f));
            }
            return;
        }
        foreach (var bc in observedBroadcasters)
        {
            float dist = 0f, sin = 0f, cos = 0f;
            float expiryNorm = 0f;
            if (bc != null)
            {
                string baseId = bc.broadcasterBaseId;
                if (string.IsNullOrEmpty(baseId)) baseId = bc.GetInstanceID().ToString();
                string prefix = baseId + "."; // base + punto
                var collectible = bc.GetComponent<CollectibleGoal>();
                if (collectible != null)
                {
                    prefix = collectible.allowedAgentId.ToString() + "." + prefix;
                }
                if (_graphNode.TryGetFirstSignalByPrefix(prefix, out var sig) && sig?.LastPropagator != null)
                {
                    var last = sig.LastPropagator.transform.position;
                    (dist, sin, cos) = RelativePolar(last - transform.position, maxPolarDistance);
                }
                // Nueva observación: tiempo restante normalizado
                if (TryGetFirstExpiryTimeByPrefix(prefix, out var expiryTime))
                {
                    float remaining = expiryTime - Time.time;
                    expiryNorm = Mathf.Clamp01(remaining / 60f); // 1 equivale a 60s
                }
            }
            AddObs(sensor, dist);
            AddObs(sensor, sin);
            AddObs(sensor, cos);
            AddObs(sensor, expiryNorm); // observación extra: tiempo restante normalizado
            _lastBroadcasterPolars.Add((dist, sin, cos, expiryNorm));
        }
    }
    /// <summary>
    /// RelativePolar: calcula la distancia normalizada y (sin, cos) del ángulo relativo del vector cartesiano
    /// respecto al forward local del agente, proyectándolo siempre sobre el plano XZ (perpendicular a Vector3.up).
    ///
    /// Definiciones:
    ///  - vPlanar = proyección de v sobre el plano XZ (Vector3.up como normal).
    ///  - distNorm = clamp(|vPlanar| / maxPolarDistance, 0, 1) si maxPolarDistance > 0, en otro caso 0.
    ///  - d = vPlanar.normalized (si magnitud > eps).
    ///  - cos = dot(forward, d), sin = dot(right, d), ambos en [-1,1].
    ///
    /// Uso típico: convertir un delta de posición o un vector de velocidad en 3 scalars (magnitud normalizada y orientación relativa como seno/coseno) para observaciones ML.
    /// </summary>
    /// <param name="cartesianVector">Vector en coordenadas mundiales.</param>
    /// <param name="maxPolarDistance">Distancia máxima para normalizar la magnitud (si &lt;= 0 -> 0).</param>
    /// <returns>Tupla (distNorm, sin, cos) donde sin = dot(right,dir) y cos = dot(forward,dir).</returns>
    public (float distNorm, float sin, float cos) RelativePolar(Vector3 cartesianVector, float maxPolarDistance)
    {
        // Proyección rápida: eliminar componente Y (plano XZ) modificando la copia local del parámetro.
        cartesianVector.y = 0f; // seguro: Vector3 es struct (valor) y no afecta al llamador

        float sqrMag = cartesianVector.sqrMagnitude;
        if (sqrMag <= 1e-16f)
        {
            return (0f, 0f, 0f);
        }

        float magnitude = Mathf.Sqrt(sqrMag);
        float distNorm = (maxPolarDistance > 0f) ? Mathf.Clamp01(magnitude / maxPolarDistance) : 0f;

        Vector3 dir = cartesianVector / magnitude; // unitario
        float cos = Mathf.Clamp(Vector3.Dot(transform.forward, dir), -1f, 1f);
        float sin = Mathf.Clamp(Vector3.Dot(transform.right, dir), -1f, 1f);
        return (distNorm, sin, cos);
    }

    [Header("Debug Gizmos")]
    [Tooltip("Si está activo, dibuja una línea verde/roja indicando la dirección relativa (sin,cos) y longitud = dist sobre la cabeza del agente.")]
    public bool drawSignalDirectionGizmo = true;
    [Tooltip("Altura sobre la posición del agente para el inicio de la línea de depuración.")]
    public float gizmoHeadOffset = 1.2f;
    [Tooltip("Dibujar sólo cuando el objeto esté seleccionado (si no, siempre)." )]
    public bool gizmoOnlyWhenSelected = false;
    [Tooltip("Longitud máxima visual (en metros) que corresponde a dist=1.")]
    public float gizmoMaxLength = 3f;
    [Tooltip("Grosor aproximado (radio) del trazo simulado con varias líneas paralelas.")]
    public float gizmoThickness = 0.05f;

    private float _lastDist, _lastSin, _lastCos; // cache de la última observación (normalizada)
    // Lista de observaciones polares de broadcasters (dist, sin, cos, expiryNorm)
    private System.Collections.Generic.List<(float dist, float sin, float cos, float expiryNorm)> _lastBroadcasterPolars = new System.Collections.Generic.List<(float, float, float, float)>();

    private void ToggleDirectionGizmo()
    {
        drawSignalDirectionGizmo = !drawSignalDirectionGizmo;
    }
    private void OnDrawGizmos()
    {
        if (!drawSignalDirectionGizmo) return;
#if UNITY_EDITOR
        if (gizmoOnlyWhenSelected && !UnityEditor.Selection.Contains(gameObject)) return;
#else
        if (gizmoOnlyWhenSelected) return;
#endif
        // Línea principal (verde)
        if (TryPrepareDirectionVisual(_lastDist, _lastSin, _lastCos, out var start, out var end, out var offsets))
        {
            Gizmos.color = Color.green;
            foreach (var o in offsets)
            {
                Gizmos.DrawLine(start + o, end + o);
            }
        }
        // Líneas de broadcasters (color por expiryNorm: 0 -> rojo, 1 -> verde)
        if (_lastBroadcasterPolars != null)
        {
            for (int i = 0; i < _lastBroadcasterPolars.Count; i++)
            {
                var (dist, sin, cos, expiryNorm) = _lastBroadcasterPolars[i];
                if (TryPrepareDirectionVisual(dist, sin, cos, out var bStart, out var bEnd, out var bOffsets))
                {
                    Color c = Color.Lerp(Color.red, Color.green, Mathf.Clamp01(expiryNorm));
                    Gizmos.color = c;
                    foreach (var o in bOffsets)
                    {
                        Gizmos.DrawLine(bStart + o, bEnd + o);
                    }
                }
            }
        }
    }

    // ========= Runtime GL Drawing (Game View sin depender del toggle 'Gizmos') =========
    private static Material _dirMat;
    private void EnsureRuntimeMaterial()
    {
        if (_dirMat != null) return;
        Shader s = Shader.Find("Hidden/Internal-Colored");
        if (!s)
        {
            return; // silencioso; fallback a Debug.DrawLine si existe
        }
        _dirMat = new Material(s)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        _dirMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _dirMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        _dirMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        _dirMat.SetInt("_ZWrite", 0);
    }
    private void OnRenderObject()
    {
        if (!Application.isPlaying) return;
        if (!drawSignalDirectionGizmo) return;
        EnsureRuntimeMaterial();
        if (_dirMat == null) return;

        // Línea principal (verde)
        if (TryPrepareDirectionVisual(_lastDist, _lastSin, _lastCos, out var start, out var end, out var offsets))
        {
            _dirMat.SetPass(0);
            if (_dirMat.HasProperty("_ZTest")) _dirMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
            GL.Begin(GL.LINES);
            GL.Color(Color.green);
            foreach (var o in offsets)
            {
                GL.Vertex(start + o); GL.Vertex(end + o);
            }
            GL.End();
        }
        // Líneas de broadcasters (color por expiryNorm: 0 -> rojo, 1 -> verde)
        if (_lastBroadcasterPolars != null)
        {
            for (int i = 0; i < _lastBroadcasterPolars.Count; i++)
            {
                var (dist, sin, cos, expiryNorm) = _lastBroadcasterPolars[i];
                if (TryPrepareDirectionVisual(dist, sin, cos, out var bStart, out var bEnd, out var bOffsets))
                {
                    _dirMat.SetPass(0);
                    if (_dirMat.HasProperty("_ZTest")) _dirMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
                    GL.Begin(GL.LINES);
                    Color c = Color.Lerp(Color.red, Color.green, Mathf.Clamp01(expiryNorm));
                    GL.Color(c);
                    foreach (var o in bOffsets)
                    {
                        GL.Vertex(bStart + o); GL.Vertex(bEnd + o);
                    }
                    GL.End();
                }
            }
        }
    }

    // Versión parametrizada para dibujar cualquier dirección polar
    private bool TryPrepareDirectionVisual(float dist, float sin, float cos, out Vector3 start, out Vector3 end, out Vector3[] offsets)
    {
        start = end = Vector3.zero;
        offsets = System.Array.Empty<Vector3>();
        if (dist <= 0f) return false;
        Vector3 dir = transform.forward * cos + transform.right * sin;
        if (dir.sqrMagnitude < 1e-10f) return false;
        dir.Normalize();
        float visualLen = Mathf.Clamp01(dist) * Mathf.Max(0.01f, gizmoMaxLength);
        start = transform.position + Vector3.up * gizmoHeadOffset;
        end = start + dir * visualLen;

        float r = Mathf.Max(0f, gizmoThickness);
        if (r <= 0.0001f)
        {
            offsets = new Vector3[] { Vector3.zero };
        }
        else
        {
            // Base ortonormal estable usando world up, con fallback.
            Vector3 up = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(up, dir)) > 0.95f) up = transform.right; // evitar casi paralelos
            Vector3 side = Vector3.Cross(dir, up).normalized;
            up = Vector3.Cross(side, dir).normalized;
            offsets = new Vector3[]
            {
                Vector3.zero,
                side * r,
                -side * r,
                up * r,
                -up * r,
                (side+up).normalized * r,
                (side-up).normalized * r,
                (-side+up).normalized * r,
                (-side-up).normalized * r
            };
        }
        return true;
    }

    /// <summary>
    /// Reconstruye la caché de prefijos de broadcasters: (allowedAgentId + '.')? + baseId + '.'
    /// </summary>
    public void BuildBroadcasterPrefixCache()
    {
        _cachedBroadcasterPrefixes.Clear();
        if (observedBroadcasters == null) return;
        for (int i = 0; i < observedBroadcasters.Count; i++)
        {
            var bc = observedBroadcasters[i];
            if (bc == null)
            {
                _cachedBroadcasterPrefixes.Add(null);
                continue;
            }
            string baseId = bc.broadcasterBaseId;
            if (string.IsNullOrEmpty(baseId)) baseId = bc.GetInstanceID().ToString();
            string prefix = baseId + ".";
            var collectible = bc.GetComponent<CollectibleGoal>();
            if (collectible != null)
            {
                prefix = collectible.allowedAgentId.ToString() + "." + prefix;
            }
            _cachedBroadcasterPrefixes.Add(prefix);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        BuildBroadcasterPrefixCache();
    }
    [ContextMenu("Rebuild Broadcaster Prefix Cache")] private void RebuildBroadcasterPrefixCacheMenu() => BuildBroadcasterPrefixCache();
#endif
}
