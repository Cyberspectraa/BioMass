using BioMass.Runtime.Movement;
using UnityEngine;

namespace BioMass.Runtime.Debugging
{
    public sealed class BioMassDebugHUD : MonoBehaviour
    {
        [SerializeField] private BioMassController target;
        [SerializeField] private bool visible = true;

        public void Configure(BioMassController controller) => target = controller;

        private void Update()
        {
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.f1Key.wasPressedThisFrame)
                visible = !visible;
        }

        private void OnGUI()
        {
            if (!visible || target == null)
                return;

            const int width = 315;
            GUI.Box(new Rect(16, 16, width, 168), "bioMass — Movement Lab");
            GUI.Label(new Rect(30, 44, width - 28, 22), $"Speed: {target.Speed:0.0} m/s");
            GUI.Label(new Rect(30, 66, width - 28, 22), $"Surface: {(target.HasSurface ? "attached" : "airborne")}");
            GUI.Label(new Rect(30, 88, width - 28, 22), $"Anchors: {target.AttachedTentacleCount}/{target.Tentacles.Count}");
            GUI.Label(new Rect(30, 110, width - 28, 22), $"Nodes: {target.Nodes.Count}   Stage: 1");
            GUI.Label(new Rect(30, 132, width - 28, 22), $"Input: {target.MoveInput.x:0.00}, {target.MoveInput.y:0.00}");
            GUI.Label(new Rect(30, 154, width - 28, 22), "WASD move • RMB look • F1 debug • R reset");
        }
    }
}
