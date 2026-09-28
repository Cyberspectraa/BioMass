using System.Collections;
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
        [SerializeField, Min(0.1f)] private float targetSpeed = 15f;
        [SerializeField, Min(0.1f)] private float coreAcceleration = 72f;
        [SerializeField, Min(0.1f)] private float followerAcceleration = 34f;
        [SerializeField, Min(0f)] private float adhesionAcceleration = 36f;
        [SerializeField, Min(0f)] private float detachedGravity = 14f;
        [SerializeField, Min(0f)] private float velocityBrake = 8f;

        [Header("Surface Traversal")]
        [SerializeField, Min(0.1f)] private float surfaceNormalSharpness = 15f;
        [SerializeField, Min(0f)] private float cornerGripAcceleration = 34f;

        [Header("Body Cohesion")]
        [SerializeField, Min(1f)] private float springStrength = 820f;
        [SerializeField, Min(0f)] private float springDamper = 56f;
        [SerializeField, Min(0.05f)] private float springMinDistance = 0.10f;
        [SerializeField, Min(0.1f)] private float springMaxDistance = 0.82f;
        [SerializeField, Min(0.1f)] private float compactRadius = 0.62f;
        [SerializeField, Min(0f)] private float movementStretchAllowance = 0.34f;
        [SerializeField, Min(0f)] private float clusterAcceleration = 46f;

        [Header("Break-off / Anti-Stuck")]
        [SerializeField, Min(0.5f)] private float softLeashDistance = 1.45f;
        [SerializeField, Min(0.75f)] private float breakOffDistance = 2.45f;
        [SerializeField, Min(0f)] private float emergencyReclaimAcceleration = 105f;
        [SerializeField, Min(0.01f)] private float breakOffGraceTime = 0.14f;
        [SerializeField, Min(0.02f)] private float reformDuration = 0.22f;
        [SerializeField, Min(0.01f)] private float reformSurfaceClearance = 0.16f;

        [Header("Locomotion Tentacles")]
        [SerializeField, Range(4, 18)] private int locomotionTentacleCount = 10;

        [Header("Debug")]
        [SerializeField] private bool debugPresentationEnabled = true;
        [SerializeField] private bool drawGizmos = true;

        private BioMassInputReader _input;
        private BioMassSurfaceSensor _sensor;
        private BioMassNode[] _nodes;
        private BioMassNode _core;
        private readonly List<LocomotionTentacle> _tentacles = new();
        private readonly List<BioMassConnectionTendril> _connections = new();
        private readonly Dictionary<BioMassNode, Vector3> _spawnOffsets = new();
        private readonly Dictionary<BioMassNode, float> _breakTimers = new();
        private readonly HashSet<BioMassNode> _reformingNodes = new();
        private readonly Collider[] _reformOverlapBuffer = new Collider[32];

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
        public bool IsSurfaceTransitioning => _sensor != null && _sensor.HasTransitionSurface && MoveInput.sqrMagnitude > 0.01f;
        public Vector3 TransitionNormal => _sensor != null ? _sensor.TransitionNormal : _surfaceNormal;
        public Vector3 TransitionPoint => _sensor != null ? _sensor.TransitionPoint : _centerOfMass;
        public bool DebugPresentationEnabled => debugPresentationEnabled;
        public IReadOnlyList<LocomotionTentacle> Tentacles => _tentacles;
        public IReadOnlyList<BioMassNode> Nodes => _nodes;
        public int AttachedTentacleCount => _tentacles.Count(t => t != null && t.IsAttached);
        public int ReformingNodeCount => _reformingNodes.Count;
        public Vector2 MoveInput => _input != null ? _input.MoveInput : Vector2.zero;

        private void Awake()
        {
            _input = GetComponent<BioMassInputReader>();
            _sensor = GetComponent<BioMassSurfaceSensor>();
            RefreshNodes();

            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;

            foreach (BioMassNode node in _nodes)
            {
                _spawnOffsets[node] = transform.InverseTransformPoint(node.transform.position);
                _breakTimers[node] = 0f;
            }

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
            UpdateNodeRecovery();

            Vector3 preTransitionDirection = _input.GetWorldMoveDirection(_surfaceNormal);
            _sensor.Sample(_core.Body.worldCenterOfMass, preTransitionDirection, _surfaceNormal);

            Vector3 targetNormal;
            if (_sensor.HasTransitionSurface && preTransitionDirection.sqrMagnitude > 0.001f)
                targetNormal = _sensor.TransitionNormal;
            else if (_sensor.HasSurface)
                targetNormal = _sensor.SurfaceNormal;
            else
                targetNormal = Vector3.up;

            float normalBlend = 1f - Mathf.Exp(-surfaceNormalSharpness * Time.fixedDeltaTime);
            _surfaceNormal = Vector3.Slerp(_surfaceNormal, targetNormal, normalBlend).normalized;
            _desiredWorldDirection = _input.GetWorldMoveDirection(_surfaceNormal);

            ApplyClusterCohesion();
            ApplyIntentForces();
            ApplySurfaceTransitionAssist();
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
            StopAllCoroutines();
            _reformingNodes.Clear();

            transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);
            _surfaceNormal = Vector3.up;

            foreach (BioMassNode node in _nodes)
            {
                if (node == null || node.Body == null)
                    continue;

                if (node.IsReforming)
                {
                    node.EndReform(Vector3.zero);
                    SetInboundSpringsForNode(node, true);
                }

                Vector3 offset = _spawnOffsets.TryGetValue(node, out Vector3 saved) ? saved : Vector3.zero;
                node.Body.position = transform.TransformPoint(offset);
                node.Body.rotation = transform.rotation;
                node.Body.linearVelocity = Vector3.zero;
                node.Body.angularVelocity = Vector3.zero;
                _breakTimers[node] = 0f;
            }

            foreach (LocomotionTentacle tentacle in _tentacles)
                tentacle?.ForceRelease();
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

            GameObject visualRoot = new GameObject("ConnectiveTendrils");
            visualRoot.transform.SetParent(transform, false);
            int connectionIndex = 0;

            for (int i = 0; i < _nodes.Length; i++)
            {
                BioMassNode node = _nodes[i];
                if (node == _core)
                    continue;

                AddSpring(node, _core);
                CreateConnectionTendril(visualRoot.transform, node, _core, connectionIndex++);

                if (i > 0)
                {
                    BioMassNode previous = _nodes[i - 1];
                    if (previous != null && previous != _core && previous != node)
                    {
                        AddSpring(node, previous, 0.68f);
                        CreateConnectionTendril(visualRoot.transform, node, previous, connectionIndex++);
                    }
                }
            }
        }

        private void CreateConnectionTendril(Transform root, BioMassNode a, BioMassNode b, int index)
        {
            GameObject go = new GameObject($"Connection_{index:00}_{a.name}_{b.name}");
            go.transform.SetParent(root, false);
            go.AddComponent<LineRenderer>();

            BioMassConnectionTendril tendril = go.AddComponent<BioMassConnectionTendril>();
            tendril.Initialize(a, b, index);
            _connections.Add(tendril);
        }

        private void AddSpring(BioMassNode from, BioMassNode to, float strengthScale = 1f)
        {
            SpringJoint spring = from.gameObject.AddComponent<SpringJoint>();
            spring.connectedBody = to.Body;
            spring.autoConfigureConnectedAnchor = false;
            spring.anchor = Vector3.zero;
            spring.connectedAnchor = Vector3.zero;
            spring.spring = springStrength * strengthScale;
            spring.damper = springDamper;
            spring.minDistance = springMinDistance;
            spring.maxDistance = springMaxDistance;
            spring.tolerance = 0.04f;
            spring.enableCollision = false;
            spring.enablePreprocessing = true;
        }

        private void IgnoreSelfCollisions()
        {
            Collider[] colliders = _nodes
                .Select(n => n.GetComponent<Collider>())
                .Where(c => c != null)
                .ToArray();

            for (int i = 0; i < colliders.Length; i++)
            for (int j = i + 1; j < colliders.Length; j++)
                Physics.IgnoreCollision(colliders[i], colliders[j], true);
        }

        private void BuildLocomotionTentacles()
        {
            if (_nodes.Length == 0)
                return;

            Transform existingRoot = transform.Find("LocomotionTentacles");
            GameObject root = existingRoot != null
                ? existingRoot.gameObject
                : new GameObject("LocomotionTentacles");

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

                tentacle.Initialize(this, _nodes[nodeIndex], i, locomotionTentacleCount);
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
                if (node == null || node.Body == null || node.IsReforming)
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

        private void UpdateNodeRecovery()
        {
            if (_core == null)
                return;

            Vector3 corePosition = _core.Body.worldCenterOfMass;

            foreach (BioMassNode node in _nodes)
            {
                if (node == null || node == _core || node.IsReforming)
                    continue;

                Vector3 toCore = corePosition - node.Body.worldCenterOfMass;
                float distance = toCore.magnitude;

                if (distance > softLeashDistance && distance > 0.001f)
                {
                    float excess01 = Mathf.Clamp01((distance - softLeashDistance) /
                                                    Mathf.Max(0.01f, breakOffDistance - softLeashDistance));
                    float reclaim = Mathf.Lerp(clusterAcceleration, emergencyReclaimAcceleration, excess01);
                    node.Body.AddForce(toCore.normalized * reclaim, ForceMode.Acceleration);
                }

                if (distance >= breakOffDistance)
                {
                    float timer = _breakTimers.TryGetValue(node, out float currentTimer)
                        ? currentTimer
                        : 0f;
                    timer += Time.fixedDeltaTime;
                    _breakTimers[node] = timer;

                    if (timer >= breakOffGraceTime)
                    {
                        _breakTimers[node] = 0f;
                        StartCoroutine(ReformDetachedNode(node));
                    }
                }
                else
                {
                    _breakTimers[node] = 0f;
                }
            }
        }

        private IEnumerator ReformDetachedNode(BioMassNode node)
        {
            if (node == null || node == _core || _reformingNodes.Contains(node))
                yield break;

            _reformingNodes.Add(node);

            foreach (LocomotionTentacle tentacle in _tentacles)
            {
                if (tentacle != null && tentacle.SourceNode == node)
                    tentacle.ForceRelease();
            }

            SetInboundSpringsForNode(node, false);
            node.BeginReform();

            float elapsed = 0f;
            while (elapsed < reformDuration)
            {
                if (_core == null)
                    break;

                float progress = reformDuration <= 0.001f ? 1f : elapsed / reformDuration;
                Vector3 target = FindSafeReformPosition(node);
                node.SetReformPose(target, _core.Body.rotation, progress);

                elapsed += Time.fixedDeltaTime;
                yield return new WaitForFixedUpdate();
            }

            if (_core != null)
            {
                Vector3 target = FindSafeReformPosition(node);
                node.SetReformPose(target, _core.Body.rotation, 1f);
                node.EndReform(_core.Body.linearVelocity * 0.88f);
                SetInboundSpringsForNode(node, true);
            }
            else
            {
                node.EndReform(Vector3.zero);
                SetInboundSpringsForNode(node, true);
            }

            _breakTimers[node] = 0f;
            _reformingNodes.Remove(node);
        }

        private void SetInboundSpringsForNode(BioMassNode node, bool enabled)
        {
            if (node == null || node.Body == null)
                return;

            foreach (BioMassNode other in _nodes)
            {
                if (other == null || other == node)
                    continue;

                foreach (SpringJoint spring in other.GetComponents<SpringJoint>())
                {
                    if (spring != null && spring.connectedBody == node.Body)
                        spring.enabled = enabled;
                }
            }
        }

        private Vector3 FindSafeReformPosition(BioMassNode node)
        {
            Vector3 core = _core.Body.worldCenterOfMass;
            Vector3 normal = _surfaceNormal.sqrMagnitude > 0.001f ? _surfaceNormal.normalized : Vector3.up;

            int nodeIndex = Mathf.Max(1, System.Array.IndexOf(_nodes, node));
            Vector3 storedOffset = _spawnOffsets.TryGetValue(node, out Vector3 offset)
                ? offset
                : Vector3.right * 0.3f;

            Vector3 tangentA = Vector3.ProjectOnPlane(storedOffset, normal);
            if (tangentA.sqrMagnitude < 0.01f)
            {
                tangentA = Vector3.ProjectOnPlane(Vector3.right, normal);
                if (tangentA.sqrMagnitude < 0.01f)
                    tangentA = Vector3.ProjectOnPlane(Vector3.forward, normal);
            }

            tangentA.Normalize();
            Vector3 tangentB = Vector3.Cross(normal, tangentA).normalized;

            float radialDistance = Mathf.Clamp(storedOffset.magnitude, 0.22f, 0.42f);
            float requiredRadius = Mathf.Max(0.12f, node.VisualRadius * 0.72f);

            for (int attempt = 0; attempt < 8; attempt++)
            {
                float angle = (nodeIndex * 0.91f) + attempt * Mathf.PI * 0.25f;
                Vector3 radial = (tangentA * Mathf.Cos(angle) + tangentB * Mathf.Sin(angle)) * radialDistance;
                Vector3 candidate = core + radial + normal * reformSurfaceClearance;

                int overlapCount = Physics.OverlapSphereNonAlloc(
                    candidate,
                    requiredRadius,
                    _reformOverlapBuffer,
                    ~0,
                    QueryTriggerInteraction.Ignore);

                bool blocked = false;
                for (int i = 0; i < overlapCount; i++)
                {
                    Collider hit = _reformOverlapBuffer[i];
                    if (hit == null || hit.transform.IsChildOf(transform))
                        continue;

                    blocked = true;
                    break;
                }

                if (!blocked)
                    return candidate;
            }

            return core + normal * (reformSurfaceClearance + 0.18f);
        }

        private void ApplyClusterCohesion()
        {
            if (_core == null)
                return;

            float speed01 = Mathf.Clamp01(Speed / Mathf.Max(0.1f, targetSpeed));
            float allowedRadius = compactRadius + movementStretchAllowance * speed01;
            Vector3 corePosition = _core.Body.worldCenterOfMass;

            foreach (BioMassNode node in _nodes)
            {
                if (node == _core || node.IsReforming)
                    continue;

                Vector3 toCore = corePosition - node.Body.worldCenterOfMass;
                float distance = toCore.magnitude;

                if (distance <= allowedRadius || distance < 0.001f)
                    continue;

                float excess = distance - allowedRadius;
                float acceleration = Mathf.Min(clusterAcceleration, excess * clusterAcceleration * 2.2f);
                node.Body.AddForce(toCore.normalized * acceleration, ForceMode.Acceleration);
            }
        }

        private void ApplyIntentForces()
        {
            if (_desiredWorldDirection.sqrMagnitude < 0.001f)
                return;

            Vector3 desiredVelocity = _desiredWorldDirection.normalized * targetSpeed;
            Vector3 coreDelta = desiredVelocity - _core.Body.linearVelocity;
            _core.Body.AddForce(
                Vector3.ClampMagnitude(coreDelta * 5.4f, coreAcceleration),
                ForceMode.Acceleration);

            foreach (BioMassNode node in _nodes)
            {
                if (node == _core || node.IsReforming)
                    continue;

                Vector3 relative = node.Body.worldCenterOfMass - _centerOfMass;
                float forwardness = relative.sqrMagnitude > 0.001f
                    ? Mathf.Clamp01((Vector3.Dot(relative.normalized, _desiredWorldDirection.normalized) + 1f) * 0.5f)
                    : 0.5f;

                float influence = Mathf.Lerp(0.48f, 1f, forwardness);
                Vector3 delta = desiredVelocity - node.Body.linearVelocity;

                node.Body.AddForce(
                    Vector3.ClampMagnitude(delta * 2.15f * influence, followerAcceleration),
                    ForceMode.Acceleration);
            }
        }

        private void ApplySurfaceTransitionAssist()
        {
            if (!IsSurfaceTransitioning || _core == null)
                return;

            Vector3 toGrip = TransitionPoint - _core.Body.worldCenterOfMass;
            if (toGrip.sqrMagnitude < 0.001f)
                return;

            _core.Body.AddForce(
                toGrip.normalized * cornerGripAcceleration,
                ForceMode.Acceleration);

            foreach (BioMassNode node in _nodes)
            {
                if (node == _core || node.IsReforming)
                    continue;

                Vector3 nodeToGrip = TransitionPoint - node.Body.worldCenterOfMass;
                if (nodeToGrip.sqrMagnitude > 0.001f)
                    node.Body.AddForce(
                        nodeToGrip.normalized * cornerGripAcceleration * 0.28f,
                        ForceMode.Acceleration);
            }
        }

        private void ApplySurfaceAdhesionOrGravity()
        {
            if (_sensor.HasSurface || IsSurfaceTransitioning)
            {
                foreach (BioMassNode node in _nodes)
                {
                    if (!node.IsReforming)
                        node.Body.AddForce(-_surfaceNormal * adhesionAcceleration, ForceMode.Acceleration);
                }
            }
            else
            {
                foreach (BioMassNode node in _nodes)
                {
                    if (!node.IsReforming)
                        node.Body.AddForce(Vector3.down * detachedGravity, ForceMode.Acceleration);
                }
            }
        }

        private void ApplySpeedControl()
        {
            foreach (BioMassNode node in _nodes)
            {
                if (node.IsReforming)
                    continue;

                Vector3 velocity = node.Body.linearVelocity;
                float maxSpeed = targetSpeed * 1.35f;

                if (velocity.magnitude > maxSpeed)
                {
                    node.Body.linearVelocity = Vector3.MoveTowards(
                        velocity,
                        velocity.normalized * maxSpeed,
                        velocityBrake * Time.fixedDeltaTime);
                }

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

            if (_core != null)
            {
                Gizmos.DrawWireSphere(_core.transform.position, softLeashDistance);
                Gizmos.DrawWireSphere(_core.transform.position, breakOffDistance);
            }

            if (_sensor == null)
                return;

            if (_sensor.HasTransitionSurface)
            {
                Gizmos.DrawWireSphere(_sensor.TransitionPoint, 0.16f);
                Gizmos.DrawLine(
                    _sensor.TransitionPoint,
                    _sensor.TransitionPoint + _sensor.TransitionNormal * 0.8f);
            }

            foreach (RaycastHit hit in _sensor.LastHits)
            {
                Gizmos.DrawWireSphere(hit.point, 0.045f);
                Gizmos.DrawLine(hit.point, hit.point + hit.normal * 0.25f);
            }
        }
    }
}
