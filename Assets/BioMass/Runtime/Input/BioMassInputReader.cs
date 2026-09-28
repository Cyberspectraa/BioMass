using UnityEngine;
using UnityEngine.InputSystem;

namespace BioMass.Runtime.Input
{
    public sealed class BioMassInputReader : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Camera referenceCamera;

        [Header("Camera")]
        [SerializeField, Min(0.01f)] private float mouseSensitivity = 0.11f;
        [SerializeField, Min(1f)] private float gamepadLookSpeed = 130f;

        public Vector2 MoveInput { get; private set; }
        public Vector2 LookInput { get; private set; }
        public bool ToggleDebugPressed { get; private set; }
        public bool ResetPressed { get; private set; }
        public Camera ReferenceCamera => referenceCamera;

        public void SetReferenceCamera(Camera camera) => referenceCamera = camera;

        private void Awake()
        {
            if (referenceCamera == null)
                referenceCamera = Camera.main;
        }

        private void Update()
        {
            MoveInput = ReadMove();
            LookInput = ReadLook();
            ToggleDebugPressed = Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame;
            ResetPressed = Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
        }

        public Vector3 GetWorldMoveDirection(Vector3 surfaceNormal)
        {
            if (MoveInput.sqrMagnitude < 0.0001f)
                return Vector3.zero;

            Transform cam = referenceCamera != null ? referenceCamera.transform : transform;
            Vector3 normal = surfaceNormal.sqrMagnitude > 0.001f ? surfaceNormal.normalized : Vector3.up;

            Vector3 forward = Vector3.ProjectOnPlane(cam.forward, normal);
            Vector3 right = Vector3.ProjectOnPlane(cam.right, normal);

            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.ProjectOnPlane(cam.up, normal);
            if (right.sqrMagnitude < 0.01f)
                right = Vector3.Cross(normal, forward);

            forward.Normalize();
            right.Normalize();
            Vector3 world = forward * MoveInput.y + right * MoveInput.x;
            return world.sqrMagnitude > 1f ? world.normalized : world;
        }

        private Vector2 ReadMove()
        {
            Vector2 move = Vector2.zero;

            if (Keyboard.current != null)
            {
                move.x += (Keyboard.current.dKey.isPressed ? 1f : 0f) - (Keyboard.current.aKey.isPressed ? 1f : 0f);
                move.y += (Keyboard.current.wKey.isPressed ? 1f : 0f) - (Keyboard.current.sKey.isPressed ? 1f : 0f);
            }

            if (Gamepad.current != null)
            {
                Vector2 stick = Gamepad.current.leftStick.ReadValue();
                if (stick.sqrMagnitude > move.sqrMagnitude)
                    move = stick;
            }

            return Vector2.ClampMagnitude(move, 1f);
        }

        private Vector2 ReadLook()
        {
            Vector2 look = Vector2.zero;

            if (Mouse.current != null && Mouse.current.rightButton.isPressed)
                look += Mouse.current.delta.ReadValue() * mouseSensitivity;

            if (Gamepad.current != null)
                look += Gamepad.current.rightStick.ReadValue() * gamepadLookSpeed * Time.unscaledDeltaTime;

            return look;
        }
    }
}
