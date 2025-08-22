using System.Collections.Generic;
using System; // para Action del evento
using UnityEngine;
using Unity.MLAgents;

namespace TFM.Navigation
{
    /// <summary>
    /// Representa una señal propagada por el grafo.
    /// </summary>
    public class Signal
    {
    public string SignalId; // ID del Broadcaster origen (string ahora permite sufijos p.ej. BASE.0, BASE.1)
        public float DistanceToOrigin; // Distancia acumulada por el grafo
        public GraphNode OriginNode; // Nodo origen
        public GraphNode LastPropagator; // Último nodo que propagó la señal

        public Signal(string signalId, float distanceToOrigin, GraphNode originNode, GraphNode lastPropagator)
        {
            SignalId = signalId;
            DistanceToOrigin = distanceToOrigin;
            OriginNode = originNode;
            LastPropagator = lastPropagator;
        }

        public Signal CloneWithNewPropagator(GraphNode newPropagator, float additionalDistance)
        {
            return new Signal(SignalId, DistanceToOrigin + additionalDistance, OriginNode, newPropagator);
        }
    }
    /// <summary>
    /// Componente dedicado a la funcionalidad de grafo (vecinos + pesos) separado de la lógica específica
    /// de Beacon o EvergreenGoal. Añádelo al mismo GameObject que otros scripts que quieras incluir en el grafo.
    /// </summary>
    [DisallowMultipleComponent]
    public class GraphNode : MonoBehaviour
    {
        [Header("Repeater")]
        [Tooltip("Si está activo, este nodo puede propagar señales a sus vecinos.")]
    public bool Repeater = true;
    [Header("Señales")]
    [Tooltip("Si está activo, este nodo mantiene un histórico (records) de las mejores señales vistas (distancia mínima alcanzada por ID). Ese histórico solo se borra con ClearSignalsAndRecords.")]
    public bool keepSignals = false; // Nuevo significado: registrar records
    // Señales recibidas actuales por este nodo: SignalId -> Signal (estado vivo del grafo)
    private Dictionary<string, Signal> receivedSignals = new Dictionary<string, Signal>();
    // Histórico persistente (records) de mejores distancias alcanzadas (solo si keepSignals=true). Nunca se limpia salvo ClearSignalsAndRecords.
    private Dictionary<string, Signal> signalRecords = new Dictionary<string, Signal>();

    /// <summary>
    /// Filtro para los registros de señales (por ID).
    /// </summary>
    [Tooltip("Si está presente, los registros de señales (signalRecords) se filtran por ID del agente (AgentInGraph).")]
    private string recordFilter;

    /// <summary>
        /// Evento disparado cuando una señal existente mejora su distancia (se encuentra un camino más corto).
        /// Parámetros: (esteNodo, señalActualizada, mejoraPositiva)
        /// mejoraPositiva = distanciaAnterior - nuevaDistancia (> 0).
        /// </summary>
        public event Action<GraphNode, Signal, float> OnRecordImproved;
    /// <summary>
    /// Evento disparado cuando se recibe una señal nueva (ID que antes no existía en receivedSignals).
    /// Parámetros: (esteNodo, señalRecibida)
    /// </summary>
    public event Action<GraphNode, Signal> OnNewSignalReceived;

    /// <summary>
    /// Evento disparado cuando se elimina una señal recibida (por TurnOffSignal).
    /// Parámetros: (esteNodo, señalEliminada)
    /// </summary>
    public event Action<GraphNode, Signal> OnSignalRemoved;

    [System.Serializable]
    public struct SignalDebugView
    {
        [Tooltip("ID del Broadcaster origen")] public string signalId;
        [Tooltip("Distancia acumulada calculada en este nodo")] public float distance;
        [Tooltip("True si el origen de la señal es este mismo nodo")] public bool isOriginHere;
    }

    [Header("Debug Señales (solo lectura)")]
    [Tooltip("Listado de señales conocidas por este nodo (ID y distancia mínima encontrada). Solo lectura.")]
    [SerializeField] private List<SignalDebugView> debugSignals = new List<SignalDebugView>();
    public IReadOnlyList<SignalDebugView> DebugSignals => debugSignals;
    [Header("Debug Records (solo lectura)")]
    [SerializeField, Tooltip("Listado histórico de mejores señales (distancia mínima registrada por ID). Solo lectura, se borra con ClearSignalsAndRecords.")] private List<SignalDebugView> debugSignalRecords = new List<SignalDebugView>();
    public IReadOnlyList<SignalDebugView> DebugSignalRecords => debugSignalRecords;

    [Header("Debug Contadores")]
    [Tooltip("Total de veces que este nodo ha incorporado una señal NUEVA (ID que antes no existía en receivedSignals) durante la ejecución.")]
    [SerializeField] private int newSignalsReceivedCount = 0;
    [Tooltip("Total de veces que la distancia de una señal existente ha mejorado (se ha encontrado un camino más corto).")]
    [SerializeField] private int signalImprovementsCount = 0;
    [Tooltip("Si está activo se contabilizan y muestran los contadores de nuevas señales y mejoras. Si se desactiva no se incrementan y se ocultan en el inspector.")]
    [SerializeField] private bool trackSignalCounters = false;
        /// <summary>Contador público de señales nuevas recibidas (solo lectura).</summary>
        public int NewSignalsReceivedCount => newSignalsReceivedCount;
        /// <summary>Contador público de mejoras de señal (solo lectura).</summary>
        public int SignalImprovementsCount => signalImprovementsCount;
    /// <summary>Toggle público (solo lectura) para saber si se están registrando los contadores.</summary>
    public bool TrackSignalCounters => trackSignalCounters;
        /// <summary>Reinicia los contadores de debug (no afecta a las señales almacenadas).</summary>
        public void ResetDebugCounters()
        {
            newSignalsReceivedCount = 0;
            signalImprovementsCount = 0;
        }

    [Header("Visualización Señal (simple)")]
    [Tooltip("Si está activo, la intensidad (emisión) del Renderer aumenta cuanto más cerca esté la señal más próxima a su origen.")]
    [SerializeField] private bool visualizeSignalIntensity = false;
    [Tooltip("Color base de emisión según la señal más cercana.")]
    [SerializeField] private Color signalEmissionColor = Color.yellow;
    [Tooltip("Factor multiplicador de intensidad. Intensidad = factor / (1 + distanciaMin).")]
    [SerializeField, Min(0.01f)] private float intensityFactor = 3f;
    [Tooltip("Si se activa intentará instanciar el material y habilitar el keyword _EMISSION si es necesario.")]
    [SerializeField] private bool forceEnableEmissionKeyword = true;

    // Propiedades de color de emisión potenciales en distintos shaders (Standard, URP, HDRP)
    private static readonly string[] PossibleEmissionProps =
    {
        "_EmissionColor",      // Standard / URP
        "_EmissiveColor",      // HDRP (linear)
        "_EmissiveColorLDR",   // HDRP (LDR)
        "_BaseColor",          // Fallback (affects albedo, no auténtica emisión)
        "_Color"               // Último fallback
    };

    private string _emissionPropName; // Detectada dinámicamente

    private Renderer _renderer;
    private MaterialPropertyBlock _mpb;

        void Awake()
        {
            recordFilter = (GetComponent<AgentInGraph>() is AgentInGraph agent) ? agent.agentId.ToString() + "." : null;
        }

        private void UpdateDebugSignals()
        {
            debugSignals.Clear();
            debugSignals.Capacity = Mathf.Max(debugSignals.Capacity, receivedSignals.Count);
            foreach (var kvp in receivedSignals)
            {
                var s = kvp.Value;
                debugSignals.Add(new SignalDebugView
                {
                    signalId = kvp.Key,
                    distance = s.DistanceToOrigin,
                    isOriginHere = (s.OriginNode == this)
                });
            }
            // Records
            debugSignalRecords.Clear();
            if (keepSignals)
            {
                debugSignalRecords.Capacity = Mathf.Max(debugSignalRecords.Capacity, signalRecords.Count);
                foreach (var kvp in signalRecords)
                {
                    var s = kvp.Value;
                    debugSignalRecords.Add(new SignalDebugView
                    {
                        signalId = kvp.Key,
                        distance = s.DistanceToOrigin,
                        isOriginHere = (s.OriginNode == this)
                    });
                }
            }
        }

        // Actualiza el histórico de records (mínima distancia alcanzada) si procede.
        private void UpdateRecord(Signal candidate)
        {
            if (!keepSignals || candidate == null) return;
            // Filtro por recordFilter: descartar si no comienza por el filtro
            if (!string.IsNullOrEmpty(recordFilter) && (candidate.SignalId == null || !candidate.SignalId.StartsWith(recordFilter, StringComparison.Ordinal))) return;
            if (signalRecords.TryGetValue(candidate.SignalId, out var existing))
            {
                if (candidate.DistanceToOrigin < existing.DistanceToOrigin)
                {
                    signalRecords[candidate.SignalId] = candidate;
                    float improvement = existing.DistanceToOrigin - candidate.DistanceToOrigin;
                    OnRecordImproved?.Invoke(this, candidate, improvement);
                }
            }
            else
            {
                signalRecords[candidate.SignalId] = candidate;
            }
        }

        /// <summary>
        /// Propaga una señal a los vecinos.
        /// </summary>
        public void PropagateSignal(Signal signal)
        {
            if (!TryRepeatSignal(signal)) return;
            for (int i = 0; i < neighbors.Count; i++)
            {
                var neighbor = neighbors[i];
                if (!neighbor) continue;
                float edgeWeight = (i < neighborDistances.Count) ? neighborDistances[i] : 1f;
                neighbor.ReceiveSignal(signal, this, edgeWeight);
            }
        }

        /// <summary>
        /// Recibe una señal desde un vecino y decide si la propaga.
        /// </summary>
        public void ReceiveSignal(Signal signal, GraphNode fromNode, float edgeWeight)
        {
            if (signal == null) return;
            // Nueva distancia acumulada desde el origen
            float newDistance = signal.DistanceToOrigin + edgeWeight;

            bool isNew = !receivedSignals.TryGetValue(signal.SignalId, out var existingSignal);
            if (!isNew && newDistance >= existingSignal.DistanceToOrigin)
            {
                // No mejora
                return;
            }

            var updatedSignal = new Signal(signal.SignalId, newDistance, signal.OriginNode, fromNode);
            receivedSignals[signal.SignalId] = updatedSignal;
            UpdateRecord(updatedSignal); // registrar histórico
            UpdateDebugSignals();
            PropagateSignal(updatedSignal);
            if (visualizeSignalIntensity) UpdateSignalIntensityVisual();

            if (isNew)
            {
                if (trackSignalCounters)
                    newSignalsReceivedCount++;
                    OnNewSignalReceived?.Invoke(this, updatedSignal);
            }
            else
            {
                if (trackSignalCounters)
                    signalImprovementsCount++;
            }
        }

        /// <summary>
        /// Obtiene la señal recibida por ID.
        /// </summary>
        public bool TryGetSignal(string signalId, out Signal signal)
        {
            return receivedSignals.TryGetValue(signalId, out signal);
    }
        /// <summary>
        /// Devuelve la señal con menor DistanceToOrigin cuyo ID comienza con el prefijo indicado.
        /// Útil para que un agente recupere "su" señal más cercana (p.ej. "3.").
        /// </summary>
        public bool TryGetClosestSignalByPrefix(string prefix, out Signal closest)
        {
            closest = null;
            if (string.IsNullOrEmpty(prefix) || receivedSignals.Count == 0) return false;
            float best = float.PositiveInfinity;
            foreach (var s in receivedSignals.Values)
            {
                if (s == null || s.SignalId == null) continue;
                if (!s.SignalId.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (s.DistanceToOrigin < best)
                {
                    best = s.DistanceToOrigin;
                    closest = s;
                }
            }
            return closest != null;
        }

        /// <summary>
        /// Devuelve la primera señal encontrada cuyo ID comienza con el prefijo indicado.
        /// Útil cuando no importa la más cercana, solo comprobar existencia/recuperar una referencia rápida.
        /// NOTA: El orden de iteración del diccionario no está garantizado; "primera" significa
        /// el primer elemento en el orden interno actual de <see cref="receivedSignals"/>.
        /// </summary>
        /// <param name="prefix">Prefijo a buscar (p.ej. "3.").</param>
        /// <param name="signal">Salida: la señal encontrada o null.</param>
        /// <returns>true si se encontró una señal con ese prefijo.</returns>
        public bool TryGetFirstSignalByPrefix(string prefix, out Signal signal)
        {
            signal = null;
            if (string.IsNullOrEmpty(prefix) || receivedSignals.Count == 0) return false;
            foreach (var s in receivedSignals.Values)
            {
                if (s == null || s.SignalId == null) continue;
                if (s.SignalId.StartsWith(prefix, StringComparison.Ordinal))
                {
                    signal = s;
                    return true;
                }
            }
            return false;
        }
    [Header("Registro Automático")]
    [Tooltip("Si está activo, este nodo se registra automáticamente en el GraphManager ancestro al habilitarse y se da de baja al deshabilitarse.")]
    public bool autoRegister = true;

        [Tooltip("Vecinos conectados (auto-generados por GraphManager o asignados manualmente)")] public List<GraphNode> neighbors = new List<GraphNode>();
        [SerializeField, Tooltip("Distancias / pesos paralelos a la lista 'neighbors'")] private List<float> neighborDistances = new List<float>();

        public IReadOnlyList<GraphNode> Neighbors => neighbors;
        public IReadOnlyList<float> NeighborDistances => neighborDistances;
        public Vector3 NodePosition => transform.position;

        /// <summary>
        /// Refresca el estado del nodo en el grafo (vecinos y distancias) llamando al GraphManager si está disponible.
        /// </summary>
        public void RefreshNode()
        {
            if (_manager == null)
            {
                _manager = GetComponentInParent<GraphManager>();
                if (_manager == null)
                {
    #if UNITY_2023_1_OR_NEWER
                        _manager = UnityEngine.Object.FindFirstObjectByType<GraphManager>();
    #else
                        _manager = UnityEngine.Object.FindObjectOfType<GraphManager>();
    #endif
                }
            }
            if (_manager != null)
            {
                _manager.UnregisterNode(this);
                _manager.RegisterNode(this);
            }
        }
        /// <summary>Elimina todas las aristas salientes.</summary>
        public void ClearNeighbors()
        {
            neighbors.Clear();
            neighborDistances.Clear();
            ClearSignals();
        }

        /// <summary>Añade un vecino si no existe todavía.</summary>
        public void AddNeighbor(GraphNode other, float distance)
        {
            if (!other || other == this) return;
            if (neighbors.Contains(other)) return;
            neighbors.Add(other);
            neighborDistances.Add(distance);
            // Nuevo enfoque: AddNeighbor se llama en ambos sentidos desde GraphManager (pares).
            // Aquí solo enviamos nuestras señales (si somos Repeater) hacia el nuevo vecino.
            // Cuando se ejecute la llamada recíproca (other.AddNeighbor(this,...)) ocurrirá el intercambio inverso.
            foreach (var sig in receivedSignals.Values)
            {
                if (!TryRepeatSignal(sig)) continue;
                other.ReceiveSignal(sig, this, distance);
            }
        }

        /// <summary>
        /// Elimina un vecino y su distancia paralela manteniendo sincronía.
        /// Devuelve true si existía y se eliminó.
        /// </summary>
        public bool RemoveNeighbor(GraphNode other)
        {
            if (!other) return false;
            int idx = neighbors.IndexOf(other);
            if (idx < 0) return false;
            neighbors.RemoveAt(idx);
            if (idx < neighborDistances.Count)
            {
                neighborDistances.RemoveAt(idx);
            }
            return true;
        }

        /// <summary>Obtiene el peso de la arista a 'other'.</summary>
        public bool TryGetNeighborDistance(GraphNode other, out float distance)
        {
            int idx = neighbors.IndexOf(other);
            if (idx >= 0 && idx < neighborDistances.Count)
            {
                distance = neighborDistances[idx];
                return true;
            }
            distance = 0f; return false;
        }

        private GraphManager _manager;

    protected virtual void OnEnable()
        {
            if (!autoRegister) return;
            // Buscar manager en padres sólo una vez
            if (_manager == null)
            {
                _manager = GetComponentInParent<GraphManager>();
                if (_manager == null)
                {
#if UNITY_2023_1_OR_NEWER
                        _manager = UnityEngine.Object.FindFirstObjectByType<GraphManager>();
#else
                        _manager = UnityEngine.Object.FindObjectOfType<GraphManager>();
#endif
                }
            }
            _manager?.RegisterNode(this);
        }

    protected virtual void OnDisable()
        {
            if (visualizeSignalIntensity)
            {
                ClearSignalIntensityVisual();
            }
            if (!autoRegister) return;
            ClearSignals();
            _manager?.UnregisterNode(this);
        }


        private void EnsureRenderer()
        {
            if (_renderer == null) _renderer = GetComponent<Renderer>();
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            if (visualizeSignalIntensity && _renderer != null && string.IsNullOrEmpty(_emissionPropName))
            {
                DetectEmissionProperty();
            }
        }

        private void UpdateSignalIntensityVisual()
        {
            EnsureRenderer();
            if (_renderer == null) return; // No hay renderer, nada que hacer.
            if (string.IsNullOrEmpty(_emissionPropName)) return; // No se encontró propiedad adecuada.

            float minDist = float.PositiveInfinity;
            foreach (var s in receivedSignals.Values)
            {
                if (s.DistanceToOrigin < minDist)
                    minDist = s.DistanceToOrigin;
            }

            float intensity = 0f;
            if (minDist < float.PositiveInfinity)
            {
                intensity = intensityFactor / (1f + Mathf.Max(0f, minDist));
            }

            // Evitar valores enormes
            intensity = Mathf.Clamp(intensity, 0f, 10f);

            _renderer.GetPropertyBlock(_mpb);
            Color emission = signalEmissionColor * intensity;
            _mpb.SetColor(_emissionPropName, emission);
            _renderer.SetPropertyBlock(_mpb);
        }

        private void ClearSignalIntensityVisual()
        {
            EnsureRenderer();
            if (_renderer == null || _mpb == null || string.IsNullOrEmpty(_emissionPropName)) return;
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(_emissionPropName, Color.black);
            _renderer.SetPropertyBlock(_mpb);
        }

        private void DetectEmissionProperty()
        {
            if (_renderer == null) return;
            var sharedMat = _renderer.sharedMaterial;
            if (sharedMat == null) return;
            foreach (var prop in PossibleEmissionProps)
            {
                if (sharedMat.HasProperty(prop))
                {
                    _emissionPropName = prop;
                    break;
                }
            }
            if (string.IsNullOrEmpty(_emissionPropName)) return; // Nada encontrado.

            // Para emisión real en Standard/URP necesitamos keyword _EMISSION. Con MaterialPropertyBlock no se activa.
            if (forceEnableEmissionKeyword && _emissionPropName == "_EmissionColor")
            {
                // Instancia material para no afectar globalmente.
                var instanced = _renderer.material; // fuerza copia
                if (!instanced.IsKeywordEnabled("_EMISSION"))
                {
                    instanced.EnableKeyword("_EMISSION");
                }
            }
            // En HDRP la emisión se maneja distinto; aquí solo establecemos el color y dejamos intensidad en el factor.
        }

        /// <summary>
        /// Limpia todas las señales recibidas por este nodo.
        /// Se llama en OnDisable y al desconectarse.
        /// NOTA: No está implementada una lógica completa para propagar correctamente la pérdida total o parcial de señal.
        /// Solo debería plantearse para escenarios donde el que pierde señal es un nodo Repeater = false.
        /// </summary>
        public void ClearSignals()
        {
            // Emitir evento OnSignalRemoved por cada señal eliminada
            if (OnSignalRemoved != null && receivedSignals.Count > 0)
            {
                foreach (var sig in receivedSignals.Values)
                {
                    OnSignalRemoved.Invoke(this, sig);
                }
            }
            receivedSignals.Clear();
            UpdateDebugSignals();
            if (visualizeSignalIntensity) UpdateSignalIntensityVisual();
        }

        /// <summary>
        /// Sobrepasa el toggle keepSignals.
        /// Se llama al desde AgentInGraph en OnEpisodeBegin.
        /// </summary>
        public void ClearSignalsAndRecords()
        {
            signalRecords.Clear();
            ClearSignals();
        }

        /// <summary>
        /// Apaga (borra) una señal y propaga el apagado a los vecinos, excepto hacia LastPropagator.
        /// Solo se propaga si el nodo tenía la señal y se borra exitosamente.
        /// </summary>
        public void TurnOffSignal(string signalId)
        {
            if (receivedSignals.TryGetValue(signalId, out var signal))
            {
                // Siempre eliminar de la vista activa (record permanece)
                receivedSignals.Remove(signalId);
                    OnSignalRemoved?.Invoke(this, signal);
                UpdateDebugSignals();
                // Propagar apagado a vecinos excepto hacia LastPropagator
                for (int i = 0; i < neighbors.Count; i++)
                {
                    var neighbor = neighbors[i];
                    if (!neighbor || neighbor == signal.LastPropagator) continue;
                    neighbor.TurnOffSignal(signalId);
                }
                if (visualizeSignalIntensity) UpdateSignalIntensityVisual();
            }
        }

        /// <summary>
        /// Determina si este nodo puede repetir (propagar) la señal dada. Por defecto depende de 'Repeater'.
        /// Override en derivadas (BroadcasterNode) para forzar su propia señal.
        /// </summary>
        protected virtual bool TryRepeatSignal(Signal signal)
        {
            return Repeater;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Refrescar vista debug en editor mientras se inspecciona.
            UpdateDebugSignals();
            // Sincronizar tamaño paralelo
            if (neighborDistances == null) neighborDistances = new List<float>(neighbors.Count);
            if (neighborDistances.Count != neighbors.Count)
            {
                var tmp = new List<float>(neighbors.Count);
                for (int i = 0; i < neighbors.Count; i++)
                {
                    float val = (i < neighborDistances.Count) ? neighborDistances[i] : -1f;
                    tmp.Add(val);
                }
                neighborDistances = tmp;
            }
            // Eliminar duplicados y self
            var seen = new HashSet<GraphNode>();
            for (int i = neighbors.Count - 1; i >= 0; i--)
            {
                var n = neighbors[i];
                if (!n || n == this || !seen.Add(n))
                {
                    neighbors.RemoveAt(i);
                    if (neighborDistances.Count == neighbors.Count + 1 && i < neighborDistances.Count)
                        neighborDistances.RemoveAt(i);
                }
            }
        }

#endif
    }
}
