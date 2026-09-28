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
        private Renderer[] _renderers;
        private Collider _collider;
        private Vector3 _restScale;

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
        public bool IsReforming { get; private set; }

        public void Configure(bool isCore, float radius, float mass)
        {
            coreNode = isCore;
            visualRadius = radius;
            EnsureComponents(mass);
        }

        private void Awake()
        {
            CachePresentation();
            EnsureComponents(coreNode ? 2.1f : 1.2f);
        }

        public void BeginReform()
        {
            if (IsReforming)
                return;

            CachePresentation();
            IsReforming = true;

            foreach (Renderer renderer in _renderers)
                if (renderer != null)
                    renderer.enabled = false;

            if (_collider != null)
                _collider.enabled = false;

            foreach (SpringJoint spring in GetComponents<SpringJoint>())
                spring.enabled = false;

            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            Body.detectCollisions = false;
            Body.isKinematic = true;
        }

        public void SetReformPose(Vector3 position, Quaternion rotation, float visualProgress)
        {
            Body.position = position;
            Body.rotation = rotation;

            float t = Mathf.Clamp01(visualProgress);
            transform.localScale = Vector3.Lerp(_restScale * 0.06f, _restScale, Mathf.SmoothStep(0f, 1f, t));

            bool show = t > 0.02f;
            foreach (Renderer renderer in _renderers)
                if (renderer != null)
                    renderer.enabled = show;
        }

        public void EndReform(Vector3 inheritedVelocity)
        {
            if (!IsReforming)
                return;

            transform.localScale = _restScale;

            foreach (Renderer renderer in _renderers)
                if (renderer != null)
                    renderer.enabled = true;

            foreach (SpringJoint spring in GetComponents<SpringJoint>())
                spring.enabled = true;

            if (_collider != null)
                _collider.enabled = true;

            Body.isKinematic = false;
            Body.detectCollisions = true;
            Body.linearVelocity = inheritedVelocity;
            Body.angularVelocity = Vector3.zero;

            IsReforming = false;
        }

        private void CachePresentation()
        {
            _renderers ??= GetComponentsInChildren<Renderer>(true);
            _collider ??= GetComponent<Collider>();

            if (_restScale == Vector3.zero)
                _restScale = transform.localScale;
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
            Body.maxLinearVelocity = 30f;
            Body.maxAngularVelocity = 18f;

            SphereCollider sphere = GetComponent<SphereCollider>();
            sphere.radius = visualRadius;
            sphere.material = null;
        }
    }
}
