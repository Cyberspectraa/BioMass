using BioMass.Runtime.Input;
using BioMass.Runtime.Movement;
using UnityEngine;

namespace BioMass.Runtime.CameraSystem
{
    public sealed class BioMassCameraRig : MonoBehaviour
    {
        [SerializeField] private BioMassController target;
        [SerializeField] private BioMassInputReader input;
        [SerializeField, Min(1f)] private float distance = 8.5f;
        [SerializeField, Min(0f)] private float lookAhead = 1.4f;
        [SerializeField, Min(0.01f)] private float positionSharpness = 8f;
        [SerializeField, Min(0.01f)] private float rotationSharpness = 10f;
        [SerializeField] private float yaw = 35f;
        [SerializeField, Range(-80f, 80f)] private float pitch = 24f;

        private Vector3 _smoothedPivot;

        public void Configure(BioMassController controller, BioMassInputReader reader)
        {
            target = controller;
            input = reader;
        }

        private void Start()
        {
            if (target != null)
                _smoothedPivot = target.CenterOfMass;
        }

        private void LateUpdate()
        {
            if (target == null)
                return;

            if (input != null)
            {
                yaw += input.LookInput.x;
                pitch = Mathf.Clamp(pitch - input.LookInput.y, -15f, 72f);
            }

            Vector3 desiredPivot = target.CenterOfMass + target.AverageVelocity * (lookAhead / Mathf.Max(1f, target.Speed + 1f));
            float pT = 1f - Mathf.Exp(-positionSharpness * Time.deltaTime);
            _smoothedPivot = Vector3.Lerp(_smoothedPivot, desiredPivot, pT);

            Quaternion orbit = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 desiredPosition = _smoothedPivot - orbit * Vector3.forward * distance;
            transform.position = Vector3.Lerp(transform.position, desiredPosition, pT);

            Vector3 lookDirection = _smoothedPivot - transform.position;
            if (lookDirection.sqrMagnitude > 0.001f)
            {
                Quaternion desiredRotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
                float rT = 1f - Mathf.Exp(-rotationSharpness * Time.deltaTime);
                transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rT);
            }
        }
    }
}
