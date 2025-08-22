using UnityEngine;

/// <summary>
/// Utilidad central para generar colores consistentes a partir de un ID de agente.
/// Fórmula: hue = id * goldenRatioConjugate mod 1, S=0.7, V=0.9.
/// Id <= 0 => Color.white.
/// </summary>
public static class AgentColorUtil
{
    private const double GoldenRatioConjugate = 0.61803398875; // para dispersar tonos

    public static Color ColorFromId(int id)
    {
        if (id <= 0) return Color.white;
        float hue = (float)((id * GoldenRatioConjugate) % 1.0);
        return Color.HSVToRGB(hue, 0.7f, 0.9f);
    }
}
