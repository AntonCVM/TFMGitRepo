using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents;

/// <summary>
/// Gestiona la dificultad (curriculum) como una máquina de estados GLOBAL de la escena.
/// NO aplica recompensas directamente: solo expone el estado (difficulty) que otros componentes consultan.
/// Fases previstas:
/// 1 => 1 agente activo + (otros componentes pueden activar shaping)
/// 2 => 2 agentes activos
/// 3 => 4 agentes activos
///
/// Colocar este componente en el mismo GameObject que el <see cref="CompetitiveArea"/> o en un padre de los agentes.
/// </summary>
public class RewardSettingsManager : MonoBehaviour
{
    public static RewardSettingsManager Instance { get; private set; }

    // Ya no se usa un área por defecto; cada agente resuelve su CompetitiveArea padre.

    [Header("Dificultad (solo lectura)")]
    [SerializeField, Tooltip("Dificultad aplicada en el episodio actual (1..3)")] private int currentDifficulty = 1;

    // Seguimiento de episodio por área para saber cuándo volver a leer parámetros.
    private readonly Dictionary<CompetitiveArea, int> _lastEpisodePerArea = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("RewardSettingsManager duplicado; se reemplaza instancia estática.");
        }
        Instance = this;
    }

    /// <summary>
    /// Notificación usada por agentes al empezar su episodio. Actualiza difficulty si cambia el episodio del área.
    /// </summary>
    public void NotifyAgentEpisode(CompetitiveAgent agent)
    {
        if (agent == null) return;
        var area = agent.GetComponentInParent<CompetitiveArea>();
        if (area == null) return;
        int epi = area.GetEpisodeIndex();
        _lastEpisodePerArea.TryGetValue(area, out var last);
        if (epi != last)
        {
            float diffF = Academy.Instance.EnvironmentParameters.GetWithDefault("difficulty", 1f);
            currentDifficulty = Mathf.Clamp(Mathf.RoundToInt(diffF), 1, 3);
            _lastEpisodePerArea[area] = epi;
        }
    }

    public int GetCurrentDifficulty() => currentDifficulty;
    public bool IsPhase1 => currentDifficulty == 1;
}
