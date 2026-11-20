// Trigger volume inside the opened portal; forwards to its HubSealGate.
using UnityEngine;

namespace Hub
{
    [RequireComponent(typeof(Collider))]
    public sealed class HubPortalTrigger : MonoBehaviour
    {
        [HideInInspector] public HubSealGate gate;
        void OnTriggerEnter(Collider other) { if (gate) gate.Enter(other); }
    }
}
