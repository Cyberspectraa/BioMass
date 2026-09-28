using System.Collections.Generic;
using UnityEngine;

namespace BioMass.Runtime.Movement
{
    public sealed class BioMassSurfaceSensor : MonoBehaviour
    {
        [SerializeField] private LayerMask surfaceMask = ~0;
        [SerializeField, Min(0.1f)] private float probeDistance = 2.2f;
        [SerializeField, Min(0.01f)] private float probeRadius = 0.18f;
        [SerializeField, Range(4, 48)] private int radialProbeCount = 16;

        public bool HasSurface { get; private set; }
        public Vector3 SurfaceNormal { get; private set; } = Vector3.up;
        public Vector3 NearestPoint { get; private set; }
        public float NearestDistance { get; private set; } = float.PositiveInfinity;
        public IReadOnlyList<RaycastHit> LastHits => _hits;

        private readonly List<RaycastHit> _hits = new(32);
        private readonly RaycastHit[] _castBuffer = new RaycastHit[64];

        public void Sample(Vector3 origin, Vector3 desiredDirection, Vector3 previousNormal)
        {
            _hits.Clear();
            NearestDistance = float.PositiveInfinity;
            Vector3 weightedNormal = Vector3.zero;
            float totalWeight = 0f;

            Vector3 preferred = desiredDirection.sqrMagnitude > 0.001f ? desiredDirection.normalized : Vector3.forward;
            Vector3 previous = previousNormal.sqrMagnitude > 0.001f ? previousNormal.normalized : Vector3.up;

            SampleDirection(origin, -previous, 2.0f, ref weightedNormal, ref totalWeight);
            SampleDirection(origin, previous, 0.7f, ref weightedNormal, ref totalWeight);
            SampleDirection(origin, preferred, 1.6f, ref weightedNormal, ref totalWeight);
            SampleDirection(origin, -preferred, 0.65f, ref weightedNormal, ref totalWeight);

            for (int i = 0; i < radialProbeCount; i++)
            {
                Vector3 dir = FibonacciDirection(i, radialProbeCount);
                float directionalBias = Mathf.Lerp(0.8f, 1.35f, Mathf.Clamp01((Vector3.Dot(dir, preferred) + 1f) * 0.5f));
                SampleDirection(origin, dir, directionalBias, ref weightedNormal, ref totalWeight);
            }

            HasSurface = totalWeight > 0.001f;
            if (HasSurface)
                SurfaceNormal = Vector3.Slerp(previous, (weightedNormal / totalWeight).normalized, 0.42f).normalized;
        }

        private void SampleDirection(Vector3 origin, Vector3 direction, float bias, ref Vector3 weightedNormal, ref float totalWeight)
        {
            if (direction.sqrMagnitude < 0.0001f)
                return;

            int count = Physics.SphereCastNonAlloc(origin, probeRadius, direction.normalized, _castBuffer, probeDistance, surfaceMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _castBuffer[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(transform.root))
                    continue;

                _hits.Add(hit);
                float proximity = 1f - Mathf.Clamp01(hit.distance / probeDistance);
                float weight = Mathf.Max(0.01f, proximity * proximity * bias);
                weightedNormal += hit.normal * weight;
                totalWeight += weight;

                if (hit.distance < NearestDistance)
                {
                    NearestDistance = hit.distance;
                    NearestPoint = hit.point;
                }
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
