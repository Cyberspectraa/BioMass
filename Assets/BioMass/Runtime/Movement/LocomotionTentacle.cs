using UnityEngine;
using UnityEngine.Rendering;

namespace BioMass.Runtime.Movement
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LineRenderer))]
    public sealed class LocomotionTentacle : MonoBehaviour
    {
        private enum TentacleState { Searching, Extending, Attached, Releasing }

        [Header("Search")]
        [SerializeField, Min(0.2f)] private float searchRadius = 3.25f;
        [SerializeField, Range(8, 48)] private int searchSamples = 24;
        [SerializeField] private LayerMask surfaceMask = ~0;
        [SerializeField, Min(0f)] private float anchorSpacing = 0.72f;

        [Header("Motion")]
        [SerializeField, Min(0.1f)] private float extensionSpeed = 20f;
        [SerializeField, Min(0f)] private float pullAcceleration = 44f;
        [SerializeField, Min(0.05f)] private float idealPullDistance = 0.66f;
        [SerializeField, Min(0.1f)] private float releaseDistance = 3.2f;
        [SerializeField, Min(0.01f)] private float minHoldTime = 0.09f;
        [SerializeField, Min(0.01f)] private float releaseTime = 0.055f;

        [Header("Debug Presentation")]
        [SerializeField] private LineRenderer lineRenderer;
        [SerializeField, Range(4, 20)] private int lineSegments = 9;
        [SerializeField, Min(0.001f)] private float lineWidth = 0.055f;

        private BioMassController _controller;
        private BioMassNode _sourceNode;
        private TentacleState _state;
        private Collider _anchorCollider;
        private Vector3 _anchorLocalPoint;
        private Vector3 _anchorLocalNormal;
        private Vector3 _tip;
        private float _stateTime;
        private float _phase;
        private readonly RaycastHit[] _searchHitBuffer = new RaycastHit[32];
        private static Material s_DebugLineMaterial;

        public bool IsAttached => _state == TentacleState.Attached;
        public Vector3 CurrentAnchor => _anchorCollider != null ? _anchorCollider.transform.TransformPoint(_anchorLocalPoint) : _tip;
        public Vector3 CurrentAnchorNormal => _anchorCollider != null
            ? _anchorCollider.transform.TransformDirection(_anchorLocalNormal).normalized
            : Vector3.up;
        public BioMassNode SourceNode => _sourceNode;

        private void Awake() => EnsureLineRenderer();
        private void OnEnable() => EnsureLineRenderer();

        public void Initialize(BioMassController controller, BioMassNode sourceNode, float phase)
        {
            _controller = controller;
            _sourceNode = sourceNode;
            _phase = phase;
            _tip = sourceNode.transform.position;
            _state = TentacleState.Searching;
            EnsureLineRenderer();
        }

        private void FixedUpdate()
        {
            if (_controller == null || _sourceNode == null || _sourceNode.Body == null)
                return;

            _stateTime += Time.fixedDeltaTime;

            switch (_state)
            {
                case TentacleState.Searching: TryAcquireAnchor(); break;
                case TentacleState.Extending: UpdateExtension(); break;
                case TentacleState.Attached: UpdateAttached(); break;
                case TentacleState.Releasing: UpdateRelease(); break;
            }
        }

        private void LateUpdate() => DrawTentacle();

        private void TryAcquireAnchor()
        {
            Vector3 source = _sourceNode.Body.worldCenterOfMass;
            Vector3 desired = _controller.DesiredWorldDirection;
            Vector3 normal = _controller.SurfaceNormal;

            float bestScore = float.NegativeInfinity;
            RaycastHit bestHit = default;

            for (int i = 0; i < searchSamples; i++)
            {
                Vector3 baseDir = FibonacciDirection((i + Mathf.RoundToInt(_phase * 17f)) % searchSamples, searchSamples);
                Vector3 preferredDirection = desired.sqrMagnitude > 0.01f ? desired : -normal;
                Vector3 movementBiased = Vector3.Slerp(baseDir, preferredDirection, 0.40f);
                Vector3 dir = movementBiased.normalized;

                int hitCount = Physics.SphereCastNonAlloc(
                    source,
                    0.08f,
                    dir,
                    _searchHitBuffer,
                    searchRadius,
                    surfaceMask,
                    QueryTriggerInteraction.Ignore);

                bool foundEnvironmentHit = false;
                RaycastHit candidateHit = default;
                float candidateDistance = float.PositiveInfinity;

                for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
                {
                    RaycastHit hit = _searchHitBuffer[hitIndex];
                    if (hit.collider == null || hit.collider.transform.IsChildOf(_controller.transform))
                        continue;

                    if (hit.distance < candidateDistance)
                    {
                        candidateDistance = hit.distance;
                        candidateHit = hit;
                        foundEnvironmentHit = true;
                    }
                }

                if (!foundEnvironmentHit)
                    continue;

                float score = ScoreAnchor(source, candidateHit, desired, normal);
                if (score <= bestScore)
                    continue;

                bestScore = score;
                bestHit = candidateHit;
            }

            if (bestScore < 0.22f || bestHit.collider == null)
                return;

            _anchorCollider = bestHit.collider;
            _anchorLocalPoint = _anchorCollider.transform.InverseTransformPoint(bestHit.point);
            _anchorLocalNormal = _anchorCollider.transform.InverseTransformDirection(bestHit.normal);
            _tip = source;
            ChangeState(TentacleState.Extending);
        }

        private float ScoreAnchor(Vector3 source, RaycastHit hit, Vector3 desired, Vector3 surfaceNormal)
        {
            Vector3 to = hit.point - source;
            float distanceScore = 1f - Mathf.Clamp01(to.magnitude / searchRadius);

            float movementScore = desired.sqrMagnitude > 0.01f
                ? Mathf.InverseLerp(-0.35f, 1f, Vector3.Dot(to.normalized, desired.normalized))
                : 0.5f;

            float forwardSurfaceScore = desired.sqrMagnitude > 0.01f
                ? Mathf.Clamp01(Vector3.Dot(desired.normalized, -hit.normal))
                : 0f;

            float currentSurfaceScore = Mathf.InverseLerp(-0.4f, 1f, Vector3.Dot(hit.normal, surfaceNormal));
            float transitionSurfaceScore = _controller.IsSurfaceTransitioning
                ? Mathf.Clamp01(Vector3.Dot(hit.normal, _controller.TransitionNormal))
                : 0f;

            float spacingScore = _controller.GetAnchorSpacingScore(hit.point, this, anchorSpacing);
            float noise = Mathf.PerlinNoise(_phase * 13.1f, Time.time * 0.17f) * 0.10f;

            return distanceScore * 0.20f
                 + movementScore * 0.29f
                 + forwardSurfaceScore * 0.18f
                 + currentSurfaceScore * 0.08f
                 + transitionSurfaceScore * 0.14f
                 + spacingScore * 0.11f
                 + noise;
        }

        private void UpdateExtension()
        {
            if (_anchorCollider == null)
            {
                ChangeState(TentacleState.Searching);
                return;
            }

            Vector3 anchor = CurrentAnchor;
            _tip = Vector3.MoveTowards(_tip, anchor, extensionSpeed * Time.fixedDeltaTime);

            if ((_tip - anchor).sqrMagnitude <= 0.02f * 0.02f)
                ChangeState(TentacleState.Attached);
        }

        private void UpdateAttached()
        {
            if (_anchorCollider == null)
            {
                ChangeState(TentacleState.Releasing);
                return;
            }

            Vector3 source = _sourceNode.Body.worldCenterOfMass;
            Vector3 anchor = CurrentAnchor;
            _tip = anchor;
            Vector3 toAnchor = anchor - source;
            float distance = toAnchor.magnitude;

            if (distance > 0.001f)
            {
                float stretch = Mathf.Max(0f, distance - idealPullDistance);
                float intent = _controller.DesiredWorldDirection.sqrMagnitude > 0.01f
                    ? Mathf.Lerp(0.62f, 1.35f, Mathf.Clamp01((Vector3.Dot(toAnchor.normalized, _controller.DesiredWorldDirection) + 1f) * 0.5f))
                    : 0.48f;

                _sourceNode.Body.AddForceAtPosition(
                    toAnchor.normalized * (pullAcceleration * stretch * intent),
                    source,
                    ForceMode.Acceleration);
            }

            bool behind = _controller.DesiredWorldDirection.sqrMagnitude > 0.01f &&
                          Vector3.Dot((anchor - _controller.CenterOfMass).normalized, _controller.DesiredWorldDirection) < -0.48f;

            bool wrongTransitionSurface = _controller.IsSurfaceTransitioning &&
                                          Vector3.Dot(CurrentAnchorNormal, _controller.TransitionNormal) < 0.30f;

            if (_stateTime >= minHoldTime && (distance > releaseDistance || behind || wrongTransitionSurface))
                ChangeState(TentacleState.Releasing);
        }

        private void UpdateRelease()
        {
            Vector3 source = _sourceNode.Body.worldCenterOfMass;
            _tip = Vector3.Lerp(_tip, source, 1f - Mathf.Exp(-26f * Time.fixedDeltaTime));

            if (_stateTime >= releaseTime)
            {
                _anchorCollider = null;
                ChangeState(TentacleState.Searching);
            }
        }

        private void ChangeState(TentacleState next)
        {
            _state = next;
            _stateTime = 0f;
        }

        private void EnsureLineRenderer()
        {
            if (!TryGetComponent(out LineRenderer attachedRenderer))
                attachedRenderer = gameObject.AddComponent<LineRenderer>();

            lineRenderer = attachedRenderer;
            if (lineRenderer == null)
                return;

            lineRenderer.useWorldSpace = true;
            lineRenderer.positionCount = lineSegments;
            lineRenderer.widthMultiplier = lineWidth;
            lineRenderer.numCapVertices = 4;
            lineRenderer.numCornerVertices = 3;
            lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;

            if (s_DebugLineMaterial == null)
            {
                Shader shader = GraphicsSettings.currentRenderPipeline != null
                    ? Shader.Find("Universal Render Pipeline/Unlit")
                    : Shader.Find("Sprites/Default");
                shader ??= Shader.Find("Sprites/Default");

                if (shader != null)
                {
                    s_DebugLineMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                    if (s_DebugLineMaterial.HasProperty("_BaseColor"))
                        s_DebugLineMaterial.SetColor("_BaseColor", new Color(0.35f, 0.015f, 0.02f, 1f));
                    if (s_DebugLineMaterial.HasProperty("_Color"))
                        s_DebugLineMaterial.SetColor("_Color", new Color(0.35f, 0.015f, 0.02f, 1f));
                }
            }

            if (s_DebugLineMaterial != null)
                lineRenderer.sharedMaterial = s_DebugLineMaterial;
        }

        private void DrawTentacle()
        {
            if (lineRenderer == null || _sourceNode == null)
                return;

            Vector3 start = _sourceNode.transform.position;
            Vector3 end = _state == TentacleState.Searching ? Vector3.Lerp(_tip, start, 0.4f) : _tip;
            lineRenderer.enabled = _controller == null || _controller.DebugPresentationEnabled;

            if (!lineRenderer.enabled)
                return;

            lineRenderer.positionCount = lineSegments;
            Vector3 axis = end - start;

            if (axis.sqrMagnitude < 0.0001f)
            {
                for (int i = 0; i < lineSegments; i++)
                    lineRenderer.SetPosition(i, start);
                return;
            }

            Vector3 side = Vector3.Cross(axis.normalized, _controller != null ? _controller.SurfaceNormal : Vector3.up);
            if (side.sqrMagnitude < 0.001f)
                side = Vector3.Cross(axis.normalized, Vector3.right);
            side.Normalize();

            for (int i = 0; i < lineSegments; i++)
            {
                float t = i / (float)(lineSegments - 1);
                float wave = Mathf.Sin(t * Mathf.PI) * Mathf.Sin(Time.time * 8f + _phase * 9f) * 0.11f;
                float sag = Mathf.Sin(t * Mathf.PI) * 0.08f;
                Vector3 point = Vector3.Lerp(start, end, t) + side * wave + Vector3.down * sag;
                lineRenderer.SetPosition(i, point);
            }
        }

        private static Vector3 FibonacciDirection(int index, int count)
        {
            float offset = 2f / count;
            float y = index * offset - 1f + offset * 0.5f;
            float r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            float phi = index * Mathf.PI * (3f - Mathf.Sqrt(5f));
            return new Vector3(Mathf.Cos(phi) * r, y, Mathf.Sin(phi) * r);
        }
    }
}
