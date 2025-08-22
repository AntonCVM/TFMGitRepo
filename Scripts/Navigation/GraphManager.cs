using System.Collections.Generic;
using UnityEngine;



namespace TFM.Navigation
{
    /// <summary>
    /// Gestiona un conjunto de GraphNode y construye las conexiones (aristas).
    /// </summary>
    public class GraphManager : MonoBehaviour
    {
        [Tooltip("Nodos gestionados (GraphNode). Puedes arrastrarlos manualmente o usar 'Scan Children'.")]
        public List<GraphNode> nodes = new List<GraphNode>();
        public IReadOnlyList<GraphNode> Nodes => nodes;

        [Header("Conectividad (Auto-Grafo)")]
        [Tooltip("Capas que bloquean la conexión entre beacons.")]
        public LayerMask connectionObstacleMask = (1 << 0); // Obstáculos en layer 0
        [Tooltip("Distancia máxima para conectar (0 = ilimitado)")]
        public float maxConnectionDistance = 0f;
        [Tooltip("Conexiones bidireccionales.")]
        public bool bidirectional = true;
        [Tooltip("Reconstruir automáticamente al habilitar.")]
    public bool autoRebuildOnEnable = true;

    [Header("Configuración LOS / Debug Global")]
    [Tooltip("Capas que se consideran obstáculos para los rayos de conexión (además de la distancia).")]
    public LayerMask losObstacleLayers = (1 << 0) | (1 << 8);
    [Tooltip("Color de conexión válida")] public Color connectionOkColor = Color.cyan;

    [Header("Visualización Conexiones")]
    [Tooltip("Mostrar u ocultar las líneas del grafo (Scene y Game).")]
    public bool showGraph = true;

    private CompetitiveArea _competitiveArea;

        private void OnEnable()
        {
            if (autoRebuildOnEnable) RebuildGraph();
        }

        private void Awake()
        {
            _competitiveArea = GetComponent<CompetitiveArea>();
            if (_competitiveArea != null)
            {
                _competitiveArea.OnResetEnvironment += RebuildGraph;
            }
        }
        // Suscripción a InputManager
        private void Start()
        {
            if (InputManager.Instance != null)
                InputManager.Instance.OnToggleGraphKey += ToggleGraph;


        }

        private void OnDestroy()
        {
            if (InputManager.Instance != null)
                InputManager.Instance.OnToggleGraphKey -= ToggleGraph;

            if (_competitiveArea != null)
            {
                _competitiveArea.OnResetEnvironment -= RebuildGraph;
            }
        }

        private void ToggleGraph()
        {
            showGraph = !showGraph;
        }

        /// <summary>
        /// Añadir beacon manualmente.
        /// </summary>
        public void AddNode(GraphNode n)
        {
            if (n != null && !nodes.Contains(n)) nodes.Add(n);
        }

        /// <summary>
        /// Eliminar beacon manualmente.
        /// </summary>
        public void RemoveNode(GraphNode n)
        {
            if (n != null) nodes.Remove(n);
        }

        /// <summary>
        /// Reconstruye el grafo limpiando y recalculando vecinos con raycasts.
        /// </summary>
        public void RebuildGraph()
        {
            // Limpieza previa de nulos, neighbors y disabled
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                if (nodes[i] == null)
                {
                    nodes.RemoveAt(i);
                    continue;
                }
                nodes[i].ClearNeighbors();

                if (!nodes[i].gameObject.activeInHierarchy)
                {
                    nodes.RemoveAt(i); // Eliminar nodos inactivos
                }
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                var a = nodes[i]; if (a == null) continue;
                Vector3 posA = a.transform.position;
                for (int j = i + 1; j < nodes.Count; j++)
                {
                    var b = nodes[j]; if (b == null) continue;
                    Vector3 posB = b.transform.position;
                    Vector3 diff = posB - posA; float dist = diff.magnitude;
                    if (maxConnectionDistance > 0f && dist > maxConnectionDistance) continue;
                    if (dist < 0.0001f) continue;
                    Vector3 dir = diff / dist;
                    // Si hay obstáculo antes del destino, no conectar
                    if (Physics.Raycast(posA, dir, dist - 0.01f, connectionObstacleMask, QueryTriggerInteraction.Ignore))
                        continue;
                    a.AddNeighbor(b, dist);
                    if (bidirectional) b.AddNeighbor(a, dist);
                }
            }
        }

        /// <summary>Registro público usado por GraphNode (autoRegister).</summary>
        public void RegisterNode(GraphNode node)
        {
            if (!node || nodes.Contains(node)) return;
            nodes.Add(node);
            // Construcción incremental: conectar solo este nodo con los existentes.
            IncrementalConnect(node);
        }

        /// <summary>Baja de nodo (autoRegister).</summary>
        public void UnregisterNode(GraphNode node)
        {
            if (!node) return;
            if (!nodes.Remove(node)) return;
            // Eliminación incremental: quitar referencias al nodo en vecinos.
            foreach (var n in nodes)
            {
                if (!n) continue;
                n.RemoveNeighbor(node);
            }
            node.ClearNeighbors(); // Limpia vecinos del nodo eliminado
        }

        private void IncrementalConnect(GraphNode newNode)
        {
            if (!newNode) return;
            var posA = newNode.transform.position;
            for (int i = 0; i < nodes.Count; i++)
            {
                var other = nodes[i];
                if (other == null || other == newNode) continue;
                Vector3 posB = other.transform.position;
                Vector3 diff = posB - posA; float dist = diff.magnitude;
                if (maxConnectionDistance > 0f && dist > maxConnectionDistance) continue;
                if (dist < 0.0001f) continue;
                Vector3 dir = diff / dist;
                if (Physics.Raycast(posA, dir, dist - 0.01f, connectionObstacleMask, QueryTriggerInteraction.Ignore)) continue;
                newNode.AddNeighbor(other, dist);
                if (bidirectional) other.AddNeighbor(newNode, dist);
            }
        }


        // =============== Runtime GL Drawing (Game View) ===============
        private static Material _glMat;
        private void EnsureRuntimeMaterial()
        {
            if (_glMat != null) return;
            // Shader interno simple (Built-in). Para URP/HDRP podría necesitar adaptarse.
            Shader s = Shader.Find("Hidden/Internal-Colored");
            if (!s)
            {
                Debug.LogWarning("GraphManager: No se encontró 'Hidden/Internal-Colored'. Se desactiva dibujo runtime.");
                return;
            }
            _glMat = new Material(s)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            // Habilitar canal de color
            _glMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _glMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _glMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            _glMat.SetInt("_ZWrite", 0);
        }

        private void OnRenderObject()
        {
            if (!Application.isPlaying) return; // Solo interesa en runtime/play
            if (!showGraph) return;
            if (nodes == null || nodes.Count == 0) return;
            EnsureRuntimeMaterial();
            if (_glMat == null) return;
            // Profundidad por defecto (LessEqual). Si quisieras forzar Always, podrías exponer otro toggle.
            if (_glMat.HasProperty("_ZTest")) _glMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);

            _glMat.SetPass(0);
            GL.Begin(GL.LINES);
            Color col = connectionOkColor;
            GL.Color(col);

            for (int i = 0; i < nodes.Count; i++)
            {
                var a = nodes[i];
                if (!a) continue;
                var posA = a.transform.position;
                var neighbors = a.Neighbors;
                for (int n = 0; n < neighbors.Count; n++)
                {
                    var b = neighbors[n];
                    if (!b) continue;
                    int jIndex = nodes.IndexOf(b); if (jIndex < i) continue; // evitar duplicados siempre
                    var posB = b.transform.position;
                    GL.Vertex(posA);
                    GL.Vertex(posB);
                }
            }
            GL.End();
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!showGraph || nodes == null) return;
            Gizmos.color = connectionOkColor;
            for (int i = 0; i < nodes.Count; i++)
            {
                var a = nodes[i]; if (!a) continue;
                var posA = a.transform.position;
                var neighbors = a.Neighbors;
                for (int n = 0; n < neighbors.Count; n++)
                {
                    var b = neighbors[n]; if (!b) continue;
                    int jIndex = nodes.IndexOf(b); if (jIndex < i) continue; // evitar duplicados
                    var posB = b.transform.position;
                    Gizmos.DrawLine(posA, posB);
                }
            }
        }
#endif

#if UNITY_EDITOR
        [ContextMenu("Scan Children (Add)")]
        private void ScanChildren()
        {
            foreach (var n in GetComponentsInChildren<GraphNode>())
            {
                if (!nodes.Contains(n)) nodes.Add(n);
            }
        }

        [ContextMenu("Limpiar Nodos Nulos")]
        private void CleanNulls()
        {
            nodes.RemoveAll(n => n == null);
        }

        [ContextMenu("Rebuild Graph (Raycasts)")]
        private void RebuildGraphContext()
        {
            RebuildGraph();
        }
#endif
    }
}
