using System.Text;
using Unity.MLAgents;
using UnityEngine;
using TMPro; // Se asume siempre disponible

/// <summary>
/// Muestra una tabla con el cumulative reward actual (episodio en curso) de cada agente y la suma total.
/// Simplificado: sin históricos.
/// Uso:
/// - Añade este script al mismo GameObject que el Text (UI.Text o TextMeshProUGUI).
/// - Asigna manualmente el array "agents" en el inspector (orden = filas).
/// - El campo de texto se autodescubre; puedes forzarlo también arrastrando manualmente.
/// - Ajusta updateInterval / numberFormat si quieres.
/// </summary>
public class RewardScoreboard : MonoBehaviour
{
    [Tooltip("Agentes a monitorizar (orden de filas)")] public CompetitiveAgent[] agents;

    [Tooltip("(Opcional) Texto destino; si se deja vacío se busca TMP_Text en este GameObject")] public TMP_Text textComponent;

    [Tooltip("Intervalo de actualización en segundos (0 = cada frame)")] public float updateInterval = 0.1f;
    [Tooltip("Formato numérico")] public string numberFormat = "F2";
    [Tooltip("Prefijo opcional")] public string header = "SCOREBOARD";
    [Tooltip("Colorear cada línea según el ID del agente")] public bool colorByAgentId = true;

    private float m_Timer;
    // Reutilizamos un único StringBuilder para evitar allocs frecuentes.
    private StringBuilder _sb;

    void Awake()
    {
        if (agents == null) agents = new CompetitiveAgent[0];
        AutoFindText();
    // Capacidad inicial aproximada (header + líneas por agente + total)
    int approxPerLine = 48;
    _sb = new StringBuilder( (agents.Length + 2) * approxPerLine );
    }

    void OnValidate()
    {
        AutoFindText();
    }

    void Update()
    {
        m_Timer += Time.unscaledDeltaTime;
        if (updateInterval > 0f && m_Timer < updateInterval) return;
        m_Timer = 0f;
        Refresh();
    }

    private void Refresh()
    {
        if (textComponent == null) return;
        if (agents == null) return;
        if (_sb == null) _sb = new StringBuilder();
        _sb.Length = 0;
        if (!string.IsNullOrEmpty(header)) _sb.AppendLine(header);
        float totalCurrent = 0f;
        for (int i = 0; i < agents.Length; i++)
        {
            var ag = agents[i];
            float current = (ag != null) ? ag.GetCumulativeReward() : 0f;
            int id = (ag != null) ? ag.agentId : 0; // 0 si null
            totalCurrent += current;

            // Datos de shaping (si existe AgentShaping)
            int noveltyCount = 0;
            float potential = 0f;
            bool hasShapingComp = false;
            if (ag != null)
            {
                var shapingComp = ag.GetComponent(System.Type.GetType("AgentShaping"));
                if (shapingComp != null)
                {
                    hasShapingComp = true;
                    // Reflection para acceder a campos privados _noveltyCount y _lastPotential
                    var noveltyField = shapingComp.GetType().GetField("_noveltyCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var potentialField = shapingComp.GetType().GetField("_lastPotential", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (noveltyField != null)
                    {
                        object val = noveltyField.GetValue(shapingComp);
                        if (val is int nc) noveltyCount = nc;
                    }
                    if (potentialField != null)
                    {
                        object val2 = potentialField.GetValue(shapingComp);
                        if (val2 is float p) potential = p;
                    }
                }
            }

            if (colorByAgentId)
            {
                var col = AgentColorUtil.ColorFromId(id);
                string hex = ColorUtility.ToHtmlStringRGB(col);
                _sb.Append("<color=#").Append(hex).Append(">");
            }

            // Formato multilínea más legible
            _sb.Append("Agente ").Append(id).Append('\n');
            _sb.Append("  Reward: ").Append(current.ToString(numberFormat));
            if (hasShapingComp)
            {
                _sb.Append('\n').Append("  Cells: ").Append(noveltyCount).Append('\n');
                _sb.Append("  Potential: ").Append(potential.ToString(numberFormat));
            }

            if (colorByAgentId) _sb.Append("</color>");
            _sb.AppendLine(); // fin bloque agente
            _sb.AppendLine(); // línea en blanco separadora
        }

        _sb.Append("Total: ").Append(totalCurrent.ToString(numberFormat)); // TOTAL neutro (sin color)

        textComponent.text = _sb.ToString();
    }

    [ContextMenu("Force Refresh Now")] public void ForceRefresh() => Refresh();

    private void AutoFindText()
    {
    if (textComponent != null) return;
    textComponent = GetComponent<TMP_Text>();
    }

    // Colores centralizados en AgentColorUtil
}
