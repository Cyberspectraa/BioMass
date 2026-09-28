using UnityEngine;

namespace BioMass.Runtime.Movement
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public sealed class BioMassNode : MonoBehaviour
    {
        [SerializeField] private bool coreNode;
        [SerializeField, Min(0.05f)] private float visualRadius = 0.42f;

        private Rigidbody _body;

        public Rigidbody Body
        {
            get
            {
                if (_body == null)
                    _body = GetComponent<Rigidbody>();
                return _body;
            }
        }
        public bool IsCore => coreNode;
        public float VisualRadius => visualRadius;

        public void Configure(bool isCore, float radius, float mass)
        {
            coreNode = isCore;
            visualRadius = radius;
            EnsureComponents(mass);
        }

        private void Awake()
        {
            EnsureComponents(coreNode ? 2.1f : 1.2f);
        }

        private void EnsureComponents(float mass)
        {
            _body = GetComponent<Rigidbody>();
            Body.mass = Mathf.Max(0.1f, mass);
            Body.useGravity = false;
            Body.linearDamping = 1.8f;
            Body.angularDamping = 4.5f;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            Body.maxLinearVelocity = 28f;
            Body.maxAngularVelocity = 18f;

            SphereCollider sphere = GetComponent<SphereCollider>();
            sphere.radius = visualRadius;
            sphere.material = null;
        }
    }
}
