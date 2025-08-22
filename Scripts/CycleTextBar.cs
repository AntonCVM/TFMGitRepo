using System.Text;
using UnityEngine;
using TMPro; // Usamos TMP_Text como en RewardScoreboard para consistencia

/// <summary>
/// Barra de texto (estilo terminal) para visualizar el ciclo temporal de CompetitiveArea.
/// - Usa solo caracteres y tags de color RichText (<color=#RRGGBB>). Asegúrate de que el componente TMP_Text tiene "Rich Text" activado.
/// - Subdivide el ciclo en "subdivisions" segmentos, cada uno coloreado según el agente asociado (CompetitiveArea.agents[index].agentId) o un color derivado del índice.
/// - El cálculo de color replica exactamente la lógica de RewardScoreboard (golden ratio hue, S=0.7, V=0.9) para coincidencia visual.
/// - La altura/escala no se modifica; es puramente visual.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class CycleTextBar : MonoBehaviour
{
    [Header("Refs")]
    public CompetitiveArea area;
    [Tooltip("Componente TMP_Text donde se dibuja la barra (si se deja null se obtiene en Awake)")] public TMP_Text targetText;

    [Header("Bar Settings")] public int barLength = 12;
    [Tooltip("Caracter usado para porción rellenada")] public char fillChar = '█';
    [Tooltip("Caracter usado para porción vacía")] public char emptyChar = '-';
    [Tooltip("Override local del número de subperiodos; si es <=0 se usa CompetitiveArea.cycleSubdivisions")] public int subdivisions = 0;
    [Tooltip("Mostrar segundos restantes del ciclo")] public bool showRemainingSeconds = true;
    [Tooltip("Mostrar contador de ciclos completos")] public bool showCycleCount = true;

    [Header("Update")]
    [Tooltip("Actualizar cada frame además del evento (útil si se anula el evento)")] public bool pollEveryFrame = false;

    [Header("Monospace (<mspace>)")]
    [Tooltip("Forzar ancho uniforme por caracter con etiqueta <mspace>. Si está desactivado se usa el ancho natural de la fuente.")] public bool forceMonospace = true;
    [Tooltip("Ancho por caracter en 'em' (relativo al tamaño de fuente) para <mspace>. 1 = ancho de la 'M'. Valores típicos 0.5 - 0.7 para barras compactas.")] [Min(0.1f)] public float monospaceEm = 1f;
    [Tooltip("Aplicar monoespaciado solo a la barra (si no, se aplica también a los sufijos como segundos). Recomendado ON.")] public bool monospaceBarOnly = true;

    // Cache
    private StringBuilder _sb;      // Builder final
    private StringBuilder _barSb;   // Builder solo de la barra (para envolver con <mspace>
    private int[] _cachedColorIds;  // IDs de color usados últimamente por subciclo
    private string[] _cachedColorHex; // Hex cacheado por subciclo

    void Awake()
    {
        AutoFindText();
        if (area == null)
        {
#if UNITY_2023_1_OR_NEWER
            area = Object.FindFirstObjectByType<CompetitiveArea>();
#else
            area = Object.FindObjectOfType<CompetitiveArea>();
#endif
        }
    _sb = new StringBuilder(barLength * 24);
    _barSb = new StringBuilder(barLength * 18);
        if (area != null) area.onCycleProgress.AddListener(OnCycleProgress);
        Redraw();
    }

    void OnValidate()
    {
        AutoFindText();
    }

    void OnDestroy()
    {
        if (area != null) area.onCycleProgress.RemoveListener(OnCycleProgress);
    }

    void Update()
    {
        if (pollEveryFrame) Redraw();
    }

    private void OnCycleProgress(float p) => Redraw();

    private void Redraw()
    {
        if (targetText == null)
            return;
        if (barLength <= 0) barLength = 1;
        int usedSubdivisions = subdivisions > 0 ? subdivisions : (area != null ? Mathf.Max(1, area.cycleSubdivisions) : 1);
        float progress = (area != null) ? area.GetCycleProgress() : 0f;

        _sb.Length = 0;

        // Calcular cuántos caracteres totales usaremos por línea
        int charsPerLine = barLength;
        // Para cada subciclo generamos una línea con su llenado parcial
        EnsureColorCaches(usedSubdivisions);
        for (int sub = 0; sub < usedSubdivisions; sub++)
        {
            float subStart = (float)sub / usedSubdivisions;
            float subEnd = (float)(sub + 1) / usedSubdivisions;
            float subProgressNorm = 0f;
            if (progress >= subEnd) subProgressNorm = 1f;
            else if (progress > subStart) subProgressNorm = (progress - subStart) / (subEnd - subStart);
            int filledInSub = Mathf.Clamp(Mathf.RoundToInt(subProgressNorm * charsPerLine), 0, charsPerLine);

            // Obtener color hex cacheado
            string hex = GetCachedHex(sub);

            _barSb.Length = 0;
            // Construir porciones (fila completa mismo color) -> un solo tag <color>
            _barSb.Append('<').Append("color=#").Append(hex).Append('>');
            AppendRepeat(_barSb, fillChar, filledInSub);
            AppendRepeat(_barSb, emptyChar, charsPerLine - filledInSub);
            _barSb.Append("</color>");

            if (forceMonospace)
            {
                string open = "<mspace=" + monospaceEm.ToString("0.###") + "em>";
                const string close = "</mspace>";
                if (monospaceBarOnly)
                    _sb.Append(open).Append(_barSb).Append(close);
                else
                {
                    _barSb.Insert(0, open).Append(close);
                    _sb.Append(_barSb);
                }
            }
            else _sb.Append(_barSb);
            if (sub < usedSubdivisions - 1) _sb.Append('\n');
        }

        if (area != null)
        {
            if (showRemainingSeconds && area.cycleDuration > 0f)
            {
                float remaining = Mathf.Max(0f, area.cycleDuration - area.GetCycleElapsed());
                _sb.Append('\n').Append("Nuevo ciclo en: ").Append(remaining.ToString("0.0")).Append('s');
            }
            if (showCycleCount)
            {
                int completed = area.GetCyclesCompleted();
                int total = area.cyclesPerEpisode; // puede ser 0 (ilimitado)
                _sb.Append('\n').Append("Ciclos transcurridos: ").Append(completed);
                if (total > 0)
                {
                    _sb.Append(" / ").Append(total);
                }
            }
        }
        targetText.text = _sb.ToString();
    }

    private Color GetSegmentColor(int segIndex)
    {
        // Usar agente si existe con ID válido
        if (area != null && area.agents != null && segIndex < area.agents.Length)
        {
            var ag = area.agents[segIndex];
            if (ag != null)
            {
                return AgentColorUtil.ColorFromId(ag.agentId);
            }
        }
        // Fallback: color derivado de índice (idéntico a secuencia golden ratio)
        return AgentColorUtil.ColorFromId(segIndex + 1);
    }

    private void EnsureColorCaches(int segments)
    {
        if (_cachedColorIds == null || _cachedColorIds.Length != segments)
        {
            _cachedColorIds = new int[segments];
            _cachedColorHex = new string[segments];
            for (int i = 0; i < segments; i++) _cachedColorIds[i] = int.MinValue;
        }
    }

    private string GetCachedHex(int segIndex)
    {
        int id = ComputeColorId(segIndex);
        if (_cachedColorIds[segIndex] != id || string.IsNullOrEmpty(_cachedColorHex[segIndex]))
        {
            Color c = GetSegmentColor(segIndex);
            _cachedColorHex[segIndex] = ColorUtility.ToHtmlStringRGB(c);
            _cachedColorIds[segIndex] = id;
        }
        return _cachedColorHex[segIndex];
    }

    private int ComputeColorId(int segIndex)
    {
        if (area != null && area.agents != null && segIndex < area.agents.Length)
        {
            var ag = area.agents[segIndex];
            if (ag != null && ag.agentId > 0) return ag.agentId;
        }
        return segIndex + 1; // fallback
    }

    // Helpers de StringBuilder para repetir caracteres sin allocs intermedias
    // Devuelve el propio builder para permitir chaining.
    private static void AppendRepeat(StringBuilder sb, char c, int count)
    {
        for (int i = 0; i < count; i++) sb.Append(c);
    }

    // Colores centralizados en AgentColorUtil

    private void AutoFindText()
    {
        if (targetText != null) return;
        targetText = GetComponent<TMP_Text>();
    }
}
