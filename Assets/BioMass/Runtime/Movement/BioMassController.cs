using System.Collections.Generic;
using System.Linq;
using BioMass.Runtime.Input;
using UnityEngine;

namespace BioMass.Runtime.Movement
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BioMassInputReader), typeof(BioMassSurfaceSensor))]
    public sealed class BioMassController : MonoBehaviour
    {
        [Header("Stage 1 Movement")]
        [SerializeField, Min(0.1f)] private float targetSpeed = 11.5f;
        [SerializeField, Min(0.1f)] private float coreAcceleration = 42f;
        [SerializeField, Min(0.1f)] private float followerAcceleration = 18f;
        [SerializeField, Min(0f)] private float adhesionAcceleration = 27f;
        [SerializeField, Min(0f)] private float detachedGravity = 14f;
        [SerializeField, Min(0f)] private float velocityBrake = 6f;

        [Header("Body Cohesion")]
        [SerializeField, Min(1f)] private float springStrength = 520f;
        [SerializeField, Min(0f)] private float springDamper = 38f;
        [SerializeField, Min(0.05f)] private float springMinDistance = 0.18f;
        [SerializeField, Min(0.1f)] private float springMaxDistance = 1.15f;

        [Header("Locomotion Tentacles")]
        [SerializeField, Range(2, 18)] private int locomotionTentacleCount = 8;

        [Header("Debug")]
        [SerializeField] private bool debugPresentationEnabled = true;
        [SerializeField] private bool drawGizmos = true;

        private BioMassInputReader _input;
        private BioMassSurfaceSensor _sensor;
        private BioMassNode[] _nodes;
        private BioMassNode _core;
        private readonly List<LocomotionTentacle> _tentacles = new();
        private readonly Dictionary<BioMassNode, Vector3> _spawnOffsets = new();
        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;
        private Vector3 _surfaceNormal = Vector3.up;
        private Vector3 _desiredWorldDirection;
        private Vector3 _centerOfMass;
        private Vector3 _averageVelocity;

        public Vector3 SurfaceNormal => _surfaceNormal;
        public Vector3 DesiredWorldDirection => _desiredWorldDirection;
        public Vector3 CenterOfMass => _centerOfMass;
        public Vector3 AverageVelocity => _averageVelocity;
        public float Speed => _averageVelocity.magnitude;
        public bool HasSurface => _sensor != null && _sensor.HasSurface;
        public bool DebugPresentationEnabled => debugPresentationEnabled;
        public IReadOnlyList<LocomotionTentacle> Tentacles => _tentacles;
        public IReadOnlyList<BioMassNode> Nodes => _nodes;
        public int AttachedTentacleCount => _tentacles.Count(t => t != null && t.IsAttached);
        public Vector2 MoveInput => _input != null ? _input.MoveInput : Vector2.zero;

        private void Awake()
        {
            _input = GetComponent<BioMassInputReader>();
            _sensor = GetComponent<BioMassSurfaceSensor>();
            RefreshNodes();

            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;

            foreach (BioMassNode node in _nodes)
                _spawnOffsets[node] = transform.InverseTransformPoint(node.transform.position);

            BuildCohesionNetwork();
            IgnoreSelfCollisions();
            BuildLocomotionTentacles();
            UpdateBodyMetrics();
        }

        private void Update()
        {
            if (_input.ToggleDebugPressed)
                debugPresentationEnabled = !debugPresentationEnabled;

            if (_input.ResetPressed)
                ResetCreature();
        }

        private void FixedUpdate()
        {
            if (_core == null || _nodes.Length == 0)
                return;

            UpdateBodyMetrics();
            _desiredWorldDirection = _input.GetWorldMoveDirection(_surfaceNormal);

            _sensor.Sample(_core.Body.worldCenterOfMass, _desiredWorldDirection, _surfaceNormal);
            if (_sensor.HasSurface)
                _surfaceNormal = _sensor.SurfaceNormal;
            else
                _surfaceNormal = Vector3.Slerp(_surfaceNormal, Vector3.up, 0.08f).normalized;

            _desiredWorldDirection = _input.GetWorldMoveDirection(_surfaceNormal);
            ApplyIntentForces();
            ApplySurfaceAdhesionOrGravity();
            ApplySpeedControl();
        }

        public float GetAnchorSpacingScore(Vector3 candidate, LocomotionTentacle requester, float preferredSpacing)
        {
            float nearest = float.PositiveInfinity;
            foreach (LocomotionTentacle tentacle in _tentacles)
            {
                if (tentacle == null || tentacle == requester || !tentacle.IsAttached)
                    continue;
                nearest = Mathf.Min(nearest, Vector3.Distance(candidate, tentacle.CurrentAnchor));
            }

            if (float.IsPositiveInfinity(nearest))
                return 1f;

            return Mathf.Clamp01(nearest / Mathf.Max(0.01f, preferredSpacing));
        }

        public void ResetCreature()
        {
            transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);
            foreach (BioMassNode node in _nodes)
            {
                if (node == null || node.Body == null)
                    continue;

                Vector3 offset = _spawnOffsets.TryGetValue(node, out Vector3 saved) ? saved : Vector3.zero;
                node.Body.position = transform.TransformPoint(offset);
                node.Body.rotation = transform.rotation;
                node.Body.linearVelocity = Vector3.zero;
                node.Body.angularVelocity = Vector3.zero;
            }
        }

        private void RefreshNodes()
        {
            _nodes = GetComponentsInChildren<BioMassNode>(true);
            _core = _nodes.FirstOrDefault(n => n.IsCore) ?? _nodes.FirstOrDefault();
        }

        private void BuildCohesionNetwork()
        {
            if (_core == null)
                return;

            for (int i = 0; i < _nodes.Length; i++)
            {
                BioMassNode node = _nodes[i];
                if (node == _core)
                    continue;

                AddSpring(node, _core);

                if (i > 0)
                {
                    BioMassNode previous = _nodes[i - 1];
                    if (previous != null && previous != _core && previous != node)
                        AddSpring(node, previous, 0.72f);
                }
            }
        }

        private void AddSpring(BioMassNode from, BioMassNode to, float strengthScale = 1f)
        {
            SpringJoint spring = from.gameObject.AddComponent<SpringJoint>();
            spring.connectedBody = to.Body;
            spring.autoConfigureConnectedAnchor = true;
            spring.spring = springStrength * strengthScale;
            spring.damper = springDamper;
            spring.minDistance = springMinDistance;
            spring.maxDistance = springMaxDistance;
            spring.tolerance = 0.05f;
            spring.enableCollision = false;
            spring.enablePreprocessing = true;
        }

        private void IgnoreSelfCollisions()
        {
            Collider[] colliders = _nodes.Select(n => n.GetComponent<Collider>()).Where(c => c != null).ToArray();
            for (int i = 0; i < colliders.Length; i++)
            for (int j = i + 1; j < colliders.Length; j++)
                Physics.IgnoreCollision(colliders[i], colliders[j], true);
        }

        private void BuildLocomotionTentacles()
        {
            if (_nodes.Length == 0)
                return;

            Transform existingRoot = transform.Find("LocomotionTentacles");
            GameObject root = existingRoot != null ? existingRoot.gameObject : new GameObject("LocomotionTentacles");
            root.transform.SetParent(transform, false);

            for (int i = 0; i < locomotionTentacleCount; i++)
            {
                GameObject go = new GameObject($"LocomotionTentacle_{i:00}");
                go.transform.SetParent(root.transform, false);

                go.AddComponent<LineRenderer>();
                LocomotionTentacle tentacle = go.AddComponent<LocomotionTentacle>();

                int nodeIndex = i % _nodes.Length;
                if (_nodes[nodeIndex] == _core && _nodes.Length > 1)
                    nodeIndex = (nodeIndex + 1) % _nodes.Length;

                tentacle.Initialize(this, _nodes[nodeIndex], i / (float)Mathf.Max(1, locomotionTentacleCount));
                _tentacles.Add(tentacle);
            }
        }

        private void UpdateBodyMetrics()
        {
            if (_nodes == null || _nodes.Length == 0)
                return;

            float totalMass = 0f;
            Vector3 weightedPosition = Vector3.zero;
            Vector3 weightedVelocity = Vector3.zero;

            foreach (BioMassNode node in _nodes)
            {
                if (node == null || node.Body == null)
                    continue;
                float mass = node.Body.mass;
                totalMass += mass;
                weightedPosition += node.Body.worldCenterOfMass * mass;
                weightedVelocity += node.Body.linearVelocity * mass;
            }

            if (totalMass > 0.001f)
            {
                _centerOfMass = weightedPosition / totalMass;
                _averageVelocity = weightedVelocity / totalMass;
            }
        }

        private void ApplyIntentForces()
        {
            if (_desiredWorldDirection.sqrMagnitude < 0.001f)
                return;

            Vector3 desiredVelocity = _desiredWorldDirection.normalized * targetSpeed;
            Vector3 coreDelta = desiredVelocity - _core.Body.linearVelocity;
            _core.Body.AddForce(Vector3.ClampMagnitude(coreDelta * 4.2f, coreAcceleration), ForceMode.Acceleration);

            foreach (BioMassNode node in _nodes)
            {
                if (node == _core)
                    continue;

                Vector3 relative = node.Body.worldCenterOfMass - _centerOfMass;
                float forwardness = Mathf.Clamp01((Vector3.Dot(relative.normalized, _desiredWorldDirection.normalized) + 1f) * 0.5f);
                float influence = Mathf.Lerp(0.25f, 1f, forwardness);
                Vector3 delta = desiredVelocity - node.Body.linearVelocity;
                node.Body.AddForce(Vector3.ClampMagnitude(delta * 1.4f * influence, followerAcceleration), ForceMode.Acceleration);
            }
        }

        private void ApplySurfaceAdhesionOrGravity()
        {
            if (_sensor.HasSurface)
            {
                foreach (BioMassNode node in _nodes)
                    node.Body.AddForce(-_surfaceNormal * adhesionAcceleration, ForceMode.Acceleration);
            }
            else
            {
                foreach (BioMassNode node in _nodes)
                    node.Body.AddForce(Vector3.down * detachedGravity, ForceMode.Acceleration);
            }
        }

        private void ApplySpeedControl()
        {
            foreach (BioMassNode node in _nodes)
            {
                Vector3 velocity = node.Body.linearVelocity;
                float maxSpeed = targetSpeed * 1.45f;
                if (velocity.magnitude > maxSpeed)
                    node.Body.linearVelocity = Vector3.MoveTowards(velocity, velocity.normalized * maxSpeed, velocityBrake * Time.fixedDeltaTime);

                if (_desiredWorldDirection.sqrMagnitude < 0.001f)
                {
                    Vector3 tangential = Vector3.ProjectOnPlane(node.Body.linearVelocity, _surfaceNormal);
                    node.Body.AddForce(-tangential * velocityBrake, ForceMode.Acceleration);
                }
            }
        }

        private void OnDrawGizmos()
        {
            if (!drawGizmos || !debugPresentationEnabled)
                return;

            Gizmos.DrawWireSphere(_centerOfMass, 0.18f);
            Gizmos.DrawLine(_centerOfMass, _centerOfMass + _desiredWorldDirection * 2f);
            Gizmos.DrawLine(_centerOfMass, _centerOfMass + _surfaceNormal * 1.5f);

            if (_sensor == null)
                return;

            foreach (RaycastHit hit in _sensor.LastHits)
            {
                Gizmos.DrawWireSphere(hit.point, 0.045f);
                Gizmos.DrawLine(hit.point, hit.point + hit.normal * 0.25f);
            }
        }
    }
}
