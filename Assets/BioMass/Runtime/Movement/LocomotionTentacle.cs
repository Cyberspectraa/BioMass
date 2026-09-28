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
        [SerializeField, Range(8, 48)] private int searchSamples = 26;
        [SerializeField] private LayerMask surfaceMask = ~0;
        [SerializeField, Min(0f)] private float anchorSpacing = 0.72f;

        [Header("Motion")]
        [SerializeField, Min(0.1f)] private float extensionSpeed = 20f;
        [SerializeField, Min(0f)] private float pullAcceleration = 44f;
        [SerializeField, Min(0.05f)] private float idealPullDistance = 0.66f;
        [SerializeField, Min(0.1f)] private float releaseDistance = 3.2f;
        [SerializeField, Min(0.01f)] private float minHoldTime = 0.09f;
        [SerializeField, Min(0.01f)] private float releaseTime = 0.055f;

        [Header("Prototype Presentation")]
        [SerializeField] private LineRenderer lineRenderer;

        private BioMassController _controller;
        private BioMassNode _sourceNode;
        private TentacleState _state;
        private Collider _anchorCollider;
        private Vector3 _anchorLocalPoint;
        private Vector3 _anchorLocalNormal;
        private Vector3 _tip;
        private float _stateTime;
        private float _phase;
        private int _shapeStyle;
        private int _lineSegments = 10;
        private float _lineWidth = 0.055f;
        private float _curveStrength = 0.12f;
        private Vector3 _territorySeed;

        private readonly RaycastHit[] _searchHitBuffer = new RaycastHit[32];
        private static Material s_DebugLineMaterial;

        public bool IsAttached => _state == TentacleState.Attached;
        public Vector3 CurrentAnchor => _anchorCollider != null
            ? _anchorCollider.transform.TransformPoint(_anchorLocalPoint)
            : _tip;
        public Vector3 CurrentAnchorNormal => _anchorCollider != null
            ? _anchorCollider.transform.TransformDirection(_anchorLocalNormal).normalized
            : Vector3.up;
        public BioMassNode SourceNode => _sourceNode;

        private void Awake() => EnsureLineRenderer();
        private void OnEnable() => EnsureLineRenderer();

        public void Initialize(BioMassController controller, BioMassNode sourceNode, int index, int totalCount)
        {
            _controller = controller;
            _sourceNode = sourceNode;

            int count = Mathf.Max(1, totalCount);
            _phase = index / (float)count;
            _shapeStyle = index % 6;
            _lineSegments = 8 + (index % 5) * 2;
            _lineWidth = 0.038f + (index % 4) * 0.013f;
            _curveStrength = 0.075f + (index % 5) * 0.025f;
            _territorySeed = FibonacciDirection(index, count);

            _tip = sourceNode.transform.position;
            _state = TentacleState.Searching;

            EnsureLineRenderer();
            ApplyShapeSettings();
        }

        public void ForceRelease()
        {
            _anchorCollider = null;
            _tip = _sourceNode != null ? _sourceNode.transform.position : transform.position;
            ChangeState(TentacleState.Searching);
        }

        private void FixedUpdate()
        {
            if (_controller == null || _sourceNode == null || _sourceNode.Body == null)
                return;

            if (_sourceNode.IsReforming)
            {
                ForceRelease();
                return;
            }

            _stateTime += Time.fixedDeltaTime;

            switch (_state)
            {
                case TentacleState.Searching:
                    TryAcquireAnchor();
                    break;
                case TentacleState.Extending:
                    UpdateExtension();
                    break;
                case TentacleState.Attached:
                    UpdateAttached();
                    break;
                case TentacleState.Releasing:
                    UpdateRelease();
                    break;
            }
        }

        private void LateUpdate() => DrawTentacle();

        private void TryAcquireAnchor()
        {
            Vector3 source = _sourceNode.Body.worldCenterOfMass;
            Vector3 desired = _controller.DesiredWorldDirection;
            Vector3 normal = _controller.SurfaceNormal;
            Vector3 territory = GetTerritoryDirection(normal, desired);

            float bestScore = float.NegativeInfinity;
            RaycastHit bestHit = default;

            for (int i = 0; i < searchSamples; i++)
            {
                Vector3 baseDir = FibonacciDirection(
                    (i + Mathf.RoundToInt(_phase * searchSamples)) % searchSamples,
                    searchSamples);

                float movementBias = _controller.IsSurfaceTransitioning ? 0.42f : 0.22f;
                Vector3 preferred = desired.sqrMagnitude > 0.01f ? desired : -normal;

                Vector3 dir = Vector3.Slerp(baseDir, territory, 0.32f);
                dir = Vector3.Slerp(dir, preferred, movementBias).normalized;

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

                    if (hit.collider == null ||
                        hit.collider.transform.IsChildOf(_controller.transform))
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

                float score = ScoreAnchor(
                    source,
                    candidateHit,
                    desired,
                    normal,
                    territory);

                if (score <= bestScore)
                    continue;

                bestScore = score;
                bestHit = candidateHit;
            }

            if (bestScore < 0.18f || bestHit.collider == null)
                return;

            _anchorCollider = bestHit.collider;
            _anchorLocalPoint = _anchorCollider.transform.InverseTransformPoint(bestHit.point);
            _anchorLocalNormal = _anchorCollider.transform.InverseTransformDirection(bestHit.normal);
            _tip = source;
            ChangeState(TentacleState.Extending);
        }

        private Vector3 GetTerritoryDirection(Vector3 surfaceNormal, Vector3 desired)
        {
            Vector3 normal = surfaceNormal.sqrMagnitude > 0.001f
                ? surfaceNormal.normalized
                : Vector3.up;

            Vector3 forward = desired.sqrMagnitude > 0.01f
                ? Vector3.ProjectOnPlane(desired, normal)
                : Vector3.ProjectOnPlane(Vector3.forward, normal);

            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.ProjectOnPlane(Vector3.right, normal);

            forward.Normalize();
            Vector3 right = Vector3.Cross(normal, forward).normalized;

            float angle = _phase * Mathf.PI * 2f;
            Vector3 radial = forward * Mathf.Cos(angle) + right * Mathf.Sin(angle);

            float normalBias = Mathf.Lerp(0.38f, 0.68f, (_territorySeed.y + 1f) * 0.5f);
            return (radial * (1f - normalBias) - normal * normalBias).normalized;
        }

        private float ScoreAnchor(
            Vector3 source,
            RaycastHit hit,
            Vector3 desired,
            Vector3 surfaceNormal,
            Vector3 territory)
        {
            Vector3 to = hit.point - source;
            if (to.sqrMagnitude < 0.0001f)
                return float.NegativeInfinity;

            Vector3 toDirection = to.normalized;
            float distanceScore = 1f - Mathf.Clamp01(to.magnitude / searchRadius);

            float movementScore = desired.sqrMagnitude > 0.01f
                ? Mathf.InverseLerp(-0.45f, 1f, Vector3.Dot(toDirection, desired.normalized))
                : 0.5f;

            float territoryScore = Mathf.InverseLerp(
                -0.45f,
                1f,
                Vector3.Dot(toDirection, territory));

            float forwardSurfaceScore = desired.sqrMagnitude > 0.01f
                ? Mathf.Clamp01(Vector3.Dot(desired.normalized, -hit.normal))
                : 0f;

            float currentSurfaceScore = Mathf.InverseLerp(
                -0.4f,
                1f,
                Vector3.Dot(hit.normal, surfaceNormal));

            float transitionSurfaceScore = _controller.IsSurfaceTransitioning
                ? Mathf.Clamp01(Vector3.Dot(hit.normal, _controller.TransitionNormal))
                : 0f;

            float spacingScore = _controller.GetAnchorSpacingScore(
                hit.point,
                this,
                anchorSpacing);

            float noise = Mathf.PerlinNoise(
                _phase * 13.1f,
                Time.time * 0.17f) * 0.08f;

            float movementWeight = _controller.IsSurfaceTransitioning ? 0.25f : 0.13f;
            float transitionWeight = _controller.IsSurfaceTransitioning ? 0.16f : 0.04f;

            return distanceScore * 0.16f
                 + movementScore * movementWeight
                 + territoryScore * 0.30f
                 + forwardSurfaceScore * 0.08f
                 + currentSurfaceScore * 0.07f
                 + transitionSurfaceScore * transitionWeight
                 + spacingScore * 0.14f
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
            _tip = Vector3.MoveTowards(
                _tip,
                anchor,
                extensionSpeed * Time.fixedDeltaTime);

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
                    ? Mathf.Lerp(
                        0.62f,
                        1.35f,
                        Mathf.Clamp01(
                            (Vector3.Dot(
                                toAnchor.normalized,
                                _controller.DesiredWorldDirection) + 1f) * 0.5f))
                    : 0.48f;

                _sourceNode.Body.AddForceAtPosition(
                    toAnchor.normalized * (pullAcceleration * stretch * intent),
                    source,
                    ForceMode.Acceleration);
            }

            bool behind =
                _controller.DesiredWorldDirection.sqrMagnitude > 0.01f &&
                Vector3.Dot(
                    (anchor - _controller.CenterOfMass).normalized,
                    _controller.DesiredWorldDirection) < -0.60f;

            bool wrongTransitionSurface =
                _controller.IsSurfaceTransitioning &&
                Vector3.Dot(
                    CurrentAnchorNormal,
                    _controller.TransitionNormal) < 0.30f;

            if (_stateTime >= minHoldTime &&
                (distance > releaseDistance || behind || wrongTransitionSurface))
            {
                ChangeState(TentacleState.Releasing);
            }
        }

        private void UpdateRelease()
        {
            Vector3 source = _sourceNode.Body.worldCenterOfMass;
            _tip = Vector3.Lerp(
                _tip,
                source,
                1f - Mathf.Exp(-26f * Time.fixedDeltaTime));

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
            lineRenderer.numCapVertices = 4;
            lineRenderer.numCornerVertices = 4;
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
                    s_DebugLineMaterial = new Material(shader)
                    {
                        hideFlags = HideFlags.HideAndDontSave
                    };

                    Color color = new(0.35f, 0.015f, 0.02f, 1f);

                    if (s_DebugLineMaterial.HasProperty("_BaseColor"))
                        s_DebugLineMaterial.SetColor("_BaseColor", color);

                    if (s_DebugLineMaterial.HasProperty("_Color"))
                        s_DebugLineMaterial.SetColor("_Color", color);
                }
            }

            if (s_DebugLineMaterial != null)
                lineRenderer.sharedMaterial = s_DebugLineMaterial;
        }

        private void ApplyShapeSettings()
        {
            if (lineRenderer == null)
                return;

            lineRenderer.positionCount = _lineSegments;
            lineRenderer.widthMultiplier = _lineWidth;

            switch (_shapeStyle)
            {
                case 0:
                    lineRenderer.widthCurve = new AnimationCurve(
                        new Keyframe(0f, 0.9f),
                        new Keyframe(0.72f, 0.72f),
                        new Keyframe(1f, 0.18f));
                    break;

                case 1:
                    lineRenderer.widthCurve = new AnimationCurve(
                        new Keyframe(0f, 0.55f),
                        new Keyframe(0.32f, 1f),
                        new Keyframe(1f, 0.12f));
                    break;

                case 2:
                    lineRenderer.widthCurve = new AnimationCurve(
                        new Keyframe(0f, 0.75f),
                        new Keyframe(0.5f, 0.52f),
                        new Keyframe(0.82f, 0.90f),
                        new Keyframe(1f, 0.16f));
                    break;

                default:
                    lineRenderer.widthCurve = new AnimationCurve(
                        new Keyframe(0f, 0.68f),
                        new Keyframe(0.45f, 0.86f),
                        new Keyframe(1f, 0.14f));
                    break;
            }
        }

        private void DrawTentacle()
        {
            if (lineRenderer == null || _sourceNode == null)
                return;

            if (_sourceNode.IsReforming)
            {
                lineRenderer.enabled = false;
                return;
            }

            Vector3 start = _sourceNode.transform.position;
            Vector3 end = _state == TentacleState.Searching
                ? Vector3.Lerp(_tip, start, 0.4f)
                : _tip;

            lineRenderer.enabled =
                _controller == null || _controller.DebugPresentationEnabled;

            if (!lineRenderer.enabled)
                return;

            lineRenderer.positionCount = _lineSegments;
            Vector3 axis = end - start;

            if (axis.sqrMagnitude < 0.0001f)
            {
                for (int i = 0; i < _lineSegments; i++)
                    lineRenderer.SetPosition(i, start);
                return;
            }

            Vector3 axisN = axis.normalized;
            Vector3 reference =
                Mathf.Abs(Vector3.Dot(axisN, _controller.SurfaceNormal)) > 0.88f
                    ? Vector3.right
                    : _controller.SurfaceNormal;

            Vector3 sideA = Vector3.Cross(axisN, reference).normalized;
            if (sideA.sqrMagnitude < 0.001f)
                sideA = Vector3.Cross(axisN, Vector3.forward).normalized;

            Vector3 sideB = Vector3.Cross(axisN, sideA).normalized;

            for (int i = 0; i < _lineSegments; i++)
            {
                float t = i / (float)(_lineSegments - 1);
                float envelope = Mathf.Sin(t * Mathf.PI);
                float waveA = 0f;
                float waveB = 0f;

                switch (_shapeStyle)
                {
                    case 0:
                        waveA = Mathf.Sin(t * Mathf.PI * 2f + _phase * 9f) * envelope;
                        waveB = Mathf.Sin(t * Mathf.PI + _phase * 4f) * envelope * 0.25f;
                        break;

                    case 1:
                        waveA = Mathf.Sin(t * Mathf.PI * 3f + _phase * 7f) * envelope * 0.55f;
                        waveB = Mathf.Cos(t * Mathf.PI * 2f + _phase * 5f) * envelope * 0.48f;
                        break;

                    case 2:
                        waveA = Mathf.Cos(t * Mathf.PI * 4f + Time.time * 1.7f + _phase * 8f) * envelope * 0.42f;
                        waveB = Mathf.Sin(t * Mathf.PI * 4f + Time.time * 1.7f + _phase * 8f) * envelope * 0.42f;
                        break;

                    case 3:
                        waveA = Mathf.Sin(t * Mathf.PI + _phase * 6f) * envelope * (0.35f + t * 0.9f);
                        waveB = Mathf.Sin(t * Mathf.PI * 5f + _phase * 3f) * envelope * 0.18f;
                        break;

                    case 4:
                        waveA = Mathf.Sin(t * Mathf.PI * 5f + _phase * 11f) * envelope * 0.25f;
                        waveB = Mathf.Sin((t + 0.22f) * Mathf.PI * 2f + _phase * 9f) * envelope * 0.62f;
                        break;

                    default:
                        waveA = Mathf.Sin(t * Mathf.PI * 2f + Time.time * 3.4f + _phase * 10f) * envelope * 0.60f;
                        waveB = Mathf.Cos(t * Mathf.PI * 3f - Time.time * 2.3f + _phase * 5f) * envelope * 0.33f;
                        break;
                }

                float twitch =
                    Mathf.Sin(Time.time * (5.5f + _shapeStyle * 0.3f) +
                              t * 6f + _phase * 12f) * 0.018f * envelope;

                Vector3 point = Vector3.Lerp(start, end, t);
                point += sideA * waveA * _curveStrength;
                point += sideB * waveB * _curveStrength;
                point += sideB * twitch;
                point += Vector3.down * envelope * 0.035f;

                lineRenderer.SetPosition(i, point);
            }
        }

        private static Vector3 FibonacciDirection(int index, int count)
        {
            count = Mathf.Max(1, count);
            float offset = 2f / count;
            float y = index * offset - 1f + offset * 0.5f;
            float r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            float phi = index * Mathf.PI * (3f - Mathf.Sqrt(5f));

            return new Vector3(
                Mathf.Cos(phi) * r,
                y,
                Mathf.Sin(phi) * r);
        }
    }
}
