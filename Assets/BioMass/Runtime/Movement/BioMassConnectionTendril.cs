using UnityEngine;
using UnityEngine.Rendering;

namespace BioMass.Runtime.Movement
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LineRenderer))]
    public sealed class BioMassConnectionTendril : MonoBehaviour
    {
        private BioMassNode _a;
        private BioMassNode _b;
        private LineRenderer _line;
        private float _phase;
        private int _style;
        private int _segments;
        private float _width;
        private float _curveAmount;
        private static Material s_Material;

        public void Initialize(BioMassNode a, BioMassNode b, int index)
        {
            _a = a;
            _b = b;
            _phase = index * 0.731f;
            _style = index % 5;
            _segments = 7 + (index % 5);
            _width = 0.045f + (index % 4) * 0.012f;
            _curveAmount = 0.09f + (index % 3) * 0.045f;

            EnsureRenderer();
        }

        private void Awake() => EnsureRenderer();

        private void LateUpdate()
        {
            if (_a == null || _b == null || _line == null)
                return;

            if (_a.IsReforming || _b.IsReforming)
            {
                _line.enabled = false;
                return;
            }

            _line.enabled = true;

            Vector3 start = _a.transform.position;
            Vector3 end = _b.transform.position;
            Vector3 axis = end - start;

            if (axis.sqrMagnitude < 0.0001f)
            {
                for (int i = 0; i < _segments; i++)
                    _line.SetPosition(i, start);
                return;
            }

            Vector3 axisN = axis.normalized;
            Vector3 reference = Mathf.Abs(Vector3.Dot(axisN, Vector3.up)) > 0.86f ? Vector3.right : Vector3.up;
            Vector3 sideA = Vector3.Cross(axisN, reference).normalized;
            Vector3 sideB = Vector3.Cross(axisN, sideA).normalized;

            float stretch = Mathf.Clamp01(axis.magnitude / 1.35f);
            float pulse = 0.85f + Mathf.Sin(Time.time * (2.8f + (_style * 0.23f)) + _phase) * 0.15f;

            for (int i = 0; i < _segments; i++)
            {
                float t = i / (float)(_segments - 1);
                float envelope = Mathf.Sin(t * Mathf.PI);
                float waveA;
                float waveB;

                switch (_style)
                {
                    case 0:
                        waveA = Mathf.Sin(t * Mathf.PI * 2f + _phase) * envelope;
                        waveB = Mathf.Sin(t * Mathf.PI + _phase) * envelope * 0.35f;
                        break;
                    case 1:
                        waveA = Mathf.Sin(t * Mathf.PI * 3f + _phase) * envelope * 0.55f;
                        waveB = Mathf.Cos(t * Mathf.PI * 2f + _phase) * envelope * 0.8f;
                        break;
                    case 2:
                        waveA = Mathf.Sin(t * Mathf.PI * 2f + _phase) * envelope;
                        waveB = Mathf.Cos(t * Mathf.PI * 2f + _phase) * envelope;
                        break;
                    case 3:
                        waveA = Mathf.Sin(t * Mathf.PI + _phase) * envelope * (0.45f + t);
                        waveB = Mathf.Sin(t * Mathf.PI * 4f + _phase) * envelope * 0.25f;
                        break;
                    default:
                        waveA = Mathf.Sin(t * Mathf.PI * 4f + _phase) * envelope * 0.35f;
                        waveB = Mathf.Sin((t + 0.2f) * Mathf.PI * 2f + _phase) * envelope * 0.65f;
                        break;
                }

                float motion = 0.82f + Mathf.Sin(Time.time * 4.2f + t * 5f + _phase) * 0.18f;
                Vector3 point = Vector3.Lerp(start, end, t);
                point += sideA * waveA * _curveAmount * motion * pulse;
                point += sideB * waveB * _curveAmount * (0.65f + stretch * 0.35f);
                _line.SetPosition(i, point);
            }
        }

        private void EnsureRenderer()
        {
            if (!TryGetComponent(out _line))
                _line = gameObject.AddComponent<LineRenderer>();

            _line.useWorldSpace = true;
            _line.positionCount = _segments > 1 ? _segments : 8;
            _line.widthMultiplier = _width > 0f ? _width : 0.055f;
            _line.numCapVertices = 4;
            _line.numCornerVertices = 4;
            _line.shadowCastingMode = ShadowCastingMode.Off;
            _line.receiveShadows = false;

            _line.widthCurve = new AnimationCurve(
                new Keyframe(0f, 0.42f),
                new Keyframe(0.22f, 1f),
                new Keyframe(0.78f, 0.85f),
                new Keyframe(1f, 0.34f));

            if (s_Material == null)
            {
                Shader shader = GraphicsSettings.currentRenderPipeline != null
                    ? Shader.Find("Universal Render Pipeline/Unlit")
                    : Shader.Find("Sprites/Default");
                shader ??= Shader.Find("Sprites/Default");

                if (shader != null)
                {
                    s_Material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                    Color color = new(0.22f, 0.008f, 0.012f, 1f);
                    if (s_Material.HasProperty("_BaseColor"))
                        s_Material.SetColor("_BaseColor", color);
                    if (s_Material.HasProperty("_Color"))
                        s_Material.SetColor("_Color", color);
                }
            }

            if (s_Material != null)
                _line.sharedMaterial = s_Material;
        }
    }
}
