using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents;

/// <summary>
/// Aplica shaping local (novelty + potential) a un agente según el estado global del RewardSettingsManager.
/// Debe añadirse a cada GameObject del agente. No maneja dificultad ni activación.
/// </summary>
[RequireComponent(typeof(CompetitiveAgent))]
public class AgentShaping : MonoBehaviour
{
    private CompetitiveAgent _agent;
    private CompetitiveArea _area;

    [Header("Novelty Grid")] public float cellSize = 10f;
    public float noveltyReward = 0.1f;
    public int noveltyCellCap = 0; // 0 = ilimitado

    [Header("Potential Shaping")] public float potentialScale = 0.1f;

    private HashSet<(int gx,int gz)> _visited; // episodic
    private float _lastPotential;
    private int _noveltyCount;
    private int _cachedEpisodeIndex = -1;

    private void Awake()
    {
        _agent = GetComponent<CompetitiveAgent>();
        _area = GetComponentInParent<CompetitiveArea>();
        _visited = new HashSet<(int,int)>();
    }

    public void OnEpisodeBegin()
    {
        if (_area == null) _area = GetComponentInParent<CompetitiveArea>();
        int epi = _area != null ? _area.GetEpisodeIndex() : 0;
        _cachedEpisodeIndex = epi;
        _visited.Clear();
        _noveltyCount = 0;
        _lastPotential = ComputePotential();
    }

    public void OnActionStep()
    {
        var rsm = RewardSettingsManager.Instance;
        if (rsm == null) return;
        // Solo aplicar shaping en Fase 1 (dificultad 1)
        if (!rsm.IsPhase1) return;
        ApplyNovelty();
        ApplyPotential();
    }

    private void ApplyNovelty()
    {
        if (noveltyReward <= 0f) return;
        if (noveltyCellCap > 0 && _noveltyCount >= noveltyCellCap) return;
        if (_area == null) return;
        _area.GetAreaBounds(out var center, out _);
        Vector3 rel = transform.position - center;
        int gx = Mathf.FloorToInt(rel.x / Mathf.Max(0.01f, cellSize));
        int gz = Mathf.FloorToInt(rel.z / Mathf.Max(0.01f, cellSize));
        var key = (gx, gz);
        if (_visited.Contains(key)) return;
        _visited.Add(key);
        _noveltyCount++;
        _agent.AddReward(noveltyReward);
    }

    private void ApplyPotential()
    {
        if (potentialScale <= 0f) return;
        float now = ComputePotential();
        float delta = now - _lastPotential;
        if (Mathf.Abs(delta) > 1e-6f)
        {
            _agent.AddReward(delta * potentialScale);
        }
        _lastPotential = now;
    }

    private float ComputePotential()
    {
        if (_area == null) return 0f;
        var list = _area.GetAllCollectibles();
        if (list == null) return 0f;
        float bestSqr = float.MaxValue;
        Vector3 pos = transform.position;
        foreach (var c in list)
        {
            if (c == null) continue;
            if (!c.IsActive) continue;
            if (!(c.allowedAgentId == 0 || c.allowedAgentId == _agent.agentId)) continue;
            float sqr = (c.transform.position - pos).sqrMagnitude;
            if (sqr < bestSqr) bestSqr = sqr;
        }
        if (bestSqr == float.MaxValue) return 0f;
        float dist = Mathf.Sqrt(bestSqr);
        _area.GetAreaBounds(out _, out var half);
        float maxReach = Mathf.Max(half.x, half.z) * 2f + 1e-6f;
        float norm = Mathf.Clamp01(dist / maxReach);
        return 1f - norm; // mayor potencial cuanto más cerca
    }
}
