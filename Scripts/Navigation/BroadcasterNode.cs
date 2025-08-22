using System;
using UnityEngine;
using System.Text.RegularExpressions;

namespace TFM.Navigation
{
    /// <summary>
    /// Nodo que origina su propia señal al habilitarse y la apaga al deshabilitarse.
    /// </summary>
    public class BroadcasterNode : GraphNode
    {
        [Header("Broadcaster")]
    [Tooltip("ID base (sin sufijo) de la señal originada por este Broadcaster. Vacío = InstanceID.")]
    public string broadcasterBaseId = "";
    [Tooltip("Último ID completo emitido (solo lectura). Formato BASE.N")] public string broadcasterSignalId = ""; // expuesto para debug

    private int _emissionSequence = 0; // sufijo incremental
    // Regex cache para detectar señales propias: ^(?:agent\.)?BASE\.seq$
    private Regex _ownSignalRegex;
    private string _cachedBaseForRegex;

        private bool _emitted;

        protected override void OnEnable()
        {
            base.OnEnable();
            if (string.IsNullOrEmpty(broadcasterBaseId))
            {
                // Usar InstanceID como base si no se especifica
                broadcasterBaseId = GetInstanceID().ToString();
            }
            EmitOwnSignal();
        }

        protected override void OnDisable()
        {
            if (_emitted)
            {
                TurnOffSignal(broadcasterSignalId);
                _emitted = false;
            }
            base.OnDisable();
        }

        private void EmitOwnSignal()
        {
            // Si existe un CollectibleGoal en el mismo GameObject, anteponer allowedAgentId.
            string prefix = string.Empty;
            var collectible = GetComponent<CollectibleGoal>();
            if (collectible != null)
            {
                prefix = collectible.allowedAgentId.ToString() + ".";
            }
            broadcasterSignalId = $"{prefix}{broadcasterBaseId}.{_emissionSequence++}";
            // Señal origen: distancia 0, origin y lastPropagator este mismo nodo
            var sig = new Signal(broadcasterSignalId, 0f, this, this);
            // Usamos ReceiveSignal para reutilizar la lógica unificada de incorporación/propagación.
            ReceiveSignal(sig, this, 0f);
            _emitted = true;
        }

        /// <summary>
        /// Un Broadcaster siempre repite su propia señal aunque Repeater sea false.
        /// Otras señales siguen la lógica base.
        /// </summary>
        protected override bool TryRepeatSignal(Signal signal)
        {
            if (signal != null && !string.IsNullOrEmpty(broadcasterBaseId))
            {
                if (_ownSignalRegex == null || _cachedBaseForRegex != broadcasterBaseId)
                {
                    _cachedBaseForRegex = broadcasterBaseId;
                    // Patrón: opcionalmente un bloque sin puntos y luego BASE.seq
                    // agentId restringido a dígitos para evitar falsos positivos (\d+\.)?
                    var baseEsc = Regex.Escape(broadcasterBaseId);
                    _ownSignalRegex = new Regex($"^(?:\\d+\\.)?{baseEsc}\\.\\d+$", RegexOptions.Compiled);
                }
                if (signal.SignalId != null && _ownSignalRegex.IsMatch(signal.SignalId)) return true;
            }
            return base.TryRepeatSignal(signal);
        }

#if UNITY_EDITOR
        // Valor por defecto distinto al de GraphNode: un Broadcaster no repeatea señales ajenas por defecto.
        private void Reset()
        {
            Repeater = false; // seguirá propagando su propia(s) señal(es) gracias al override de TryRepeatSignal.
        }
#endif
    }
}
