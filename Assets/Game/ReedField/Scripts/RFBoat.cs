// The small ferry boat (나룻배) moored in the river, tied to a 장승 on the bank (Kido 11-10).
// The current pushes it downstream until the rope is taut: it rides at the end of the rope, swings a little across the
// stream, surges and tugs, bobs and rocks. The rope is a thin line from the 장승 to the iron ring on the bow, nearly
// straight while it is pulled tight and sagging a touch when the boat surges back.
using UnityEngine;

namespace Reeds
{
    [ExecuteAlways]
    public class RFBoat : MonoBehaviour
    {
        [Tooltip("The boat (mesh RF_Boat; its submesh 2 is the bow ring the rope is tied to).")]
        public Transform boat;
        [Tooltip("Where the rope is tied on the 장승 / post (world point is this transform's position).")]
        public Transform tiePoint;
        public float ropeLength = 5.5f;
        [Tooltip("Direction the river flows (x, z).")]
        public Vector2 flow = new Vector2(-1, 0);
        [Tooltip("Away from the bank, into the stream (x, z). The rope runs out between this and the flow.")]
        public Vector2 offshore = new Vector2(0, -1);
        [Range(0f, 2f)] public float offshoreAmount = 0.85f;
        public float waterY = -0.32f;
        [Range(0f, 2f)] public float swing = 0.7f;      // side to side across the current (m)
        [Range(0f, 1f)] public float surge = 0.35f;     // back and forth along the rope (m)
        [Range(0f, 10f)] public float roll = 4f;        // degrees
        [Range(0f, 6f)] public float pitch = 2f;
        public float bob = 0.05f;
        public LineRenderer rope;

        Vector3 ringLocal, bowLocal;
        bool measured;

        void Measure()
        {
            measured = true;
            var mf = boat ? boat.GetComponentInChildren<MeshFilter>() : null;
            var mesh = mf ? mf.sharedMesh : null;
            if (!mesh) return;
            // bow ring = submesh 2 (only the ring uses it); bow direction = from the hull centre toward it
            var b = mesh.subMeshCount > 2 ? mesh.GetSubMesh(2).bounds : mesh.bounds;
            var toMf = boat.InverseTransformPoint(mf.transform.TransformPoint(b.center));
            ringLocal = toMf;
            bowLocal = new Vector3(ringLocal.x, 0, ringLocal.z).normalized;
        }

        void LateUpdate()
        {
            if (!boat || !tiePoint) return;
            if (!measured) Measure();
            float t = Application.isPlaying ? Time.time : (float)(Time.realtimeSinceStartupAsDouble % 3600.0);
            // the rope runs out from the bank into the stream and downstream with the current
            var down = (new Vector3(flow.x, 0, flow.y).normalized + new Vector3(offshore.x, 0, offshore.y).normalized * offshoreAmount).normalized;
            var across = Vector3.Cross(Vector3.up, down);
            var tie = tiePoint.position;

            // the current holds it out downstream; slow swing across the stream, quicker tugging surges along the rope
            float sw = (Mathf.Sin(t * 0.33f) * 0.7f + Mathf.Sin(t * 0.81f + 1.3f) * 0.3f) * swing;
            float sg = (Mathf.Sin(t * 1.1f) * 0.6f + Mathf.Sin(t * 2.3f + 0.4f) * 0.4f) * surge;
            float reach = ropeLength - Mathf.Max(0f, -sg);      // surging back gives slack, pulling out keeps it tight
            var bowFlat = new Vector3(tie.x, 0, tie.z) + (down * Mathf.Sqrt(Mathf.Max(reach * reach - sw * sw, 0.01f)) + across * sw);

            // bow points back up the rope toward the 장승, with a little yaw from the swing
            var toTie = new Vector3(tie.x - bowFlat.x, 0, tie.z - bowFlat.z).normalized;
            var yaw = Quaternion.FromToRotation(bowLocal == Vector3.zero ? Vector3.forward : bowLocal, toTie);
            yaw = Quaternion.AngleAxis(Mathf.Sin(t * 0.47f) * 4f, Vector3.up) * yaw;
            var rock = Quaternion.AngleAxis(Mathf.Sin(t * 1.3f) * roll + Mathf.Sin(t * 2.9f) * roll * 0.25f, toTie) *
                       Quaternion.AngleAxis(Mathf.Sin(t * 0.9f + 0.7f) * pitch, Vector3.Cross(Vector3.up, toTie));
            boat.rotation = rock * yaw;
            // place so the bow ring lands on bowFlat, hull riding at the waterline
            var ringWorldOffset = boat.rotation * Vector3.Scale(ringLocal, boat.lossyScale);
            float y = waterY + Mathf.Sin(t * 1.7f) * bob + Mathf.Sin(t * 3.1f + 1f) * bob * 0.3f;
            boat.position = new Vector3(bowFlat.x - ringWorldOffset.x, y, bowFlat.z - ringWorldOffset.z);

            if (rope)
            {
                var ring = boat.position + ringWorldOffset;
                float slack = Mathf.Max(0f, ropeLength - Vector3.Distance(tie, ring));
                float sag = 0.03f + Mathf.Sqrt(slack * ropeLength) * 0.35f;   // near straight when taut
                const int N = 14;
                if (rope.positionCount != N) rope.positionCount = N;
                for (int i = 0; i < N; i++)
                {
                    float u = i / (N - 1f);
                    rope.SetPosition(i, Vector3.Lerp(tie, ring, u) + Vector3.down * sag * 4f * u * (1 - u));
                }
            }
        }
    }
}
