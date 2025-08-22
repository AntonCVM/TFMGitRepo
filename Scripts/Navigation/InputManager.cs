using UnityEngine;
using System;

/// <summary>
/// Centraliza la gestión de entradas de teclado y expone eventos para suscriptores.
/// </summary>
public class InputManager : MonoBehaviour
{
    public static InputManager Instance { get; private set; }

    // Evento para la tecla R
    public event Action OnToggleDirectionGizmoKey;

    // Evento para la tecla BackQuote (toggle del grafo)
    public event Action OnToggleGraphKey;

    [Tooltip("Tecla para alternar la visualización de la dirección de la señal.")]
    public KeyCode toggleDirectionGizmoKey = KeyCode.E;

    [Tooltip("Tecla para alternar la visualización del grafo.")]
    public KeyCode toggleGraphKey = KeyCode.Q;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private void Update()
    {
        if (Application.isPlaying && Input.GetKeyDown(toggleDirectionGizmoKey))
        {
            OnToggleDirectionGizmoKey?.Invoke();
        }
        if (Application.isPlaying && Input.GetKeyDown(toggleGraphKey))
        {
            OnToggleGraphKey?.Invoke();
        }
    }
}
