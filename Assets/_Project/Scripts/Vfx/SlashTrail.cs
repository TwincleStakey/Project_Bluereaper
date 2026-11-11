using UnityEngine;

namespace BlueReaper.Vfx
{
    /// <summary>
    /// 칼 궤적 한 겹. 매 프레임 반달 모양 띠(메시)를 다시 그린다.
    /// 머리(칼끝)가 부채꼴을 쓸고 지나가면, 꼬리가 따라잡으며 사라진다.
    /// 프리팹 안에 여러 겹(빛·몸통·심·잔상)을 두고 설정값만 다르게 준다.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    public class SlashTrail : MonoBehaviour
    {
        const int Segments = 32;

        [Tooltip("칼끝이 부채꼴 끝까지 가는 시간(초)")]
        [SerializeField] float sweepTime = 0.06f;
        [Tooltip("꼬리가 머리를 따라잡는 시간(초)")]
        [SerializeField] float tailTime = 0.16f;
        [Tooltip("이만큼 늦게 출발 (잔상 겹에 사용)")]
        [SerializeField] float lag;
        [Tooltip("사거리에 곱하는 반지름 배율 (1 = 판정 끝선)")]
        [SerializeField] float radiusScale = 1f;
        [Tooltip("띠의 최대 두께(m)")]
        [SerializeField] float width = 0.45f;
        [Tooltip("땅에서 띠까지의 높이(m)")]
        [SerializeField] float height = 1.1f;

        // 토막마다 안쪽 점 1개 + 바깥쪽 점 1개
        readonly Vector3[] vertices = new Vector3[(Segments + 1) * 2];
        readonly Color[] colors = new Color[(Segments + 1) * 2];
        Mesh mesh;
        float radius, halfArc, age;

        void Awake()
        {
            // 모양은 매 프레임 바뀌어도 "어느 점끼리 삼각형인지"는 그대로라 한 번만 정한다
            mesh = new Mesh();
            var uv = new Vector2[vertices.Length];
            var triangles = new int[Segments * 6];
            for (int i = 0; i <= Segments; i++)
            {
                float u = (float)i / Segments;
                uv[i * 2] = new Vector2(u, 0f);
                uv[i * 2 + 1] = new Vector2(u, 1f);
                if (i == Segments) break;
                int v = i * 2, t = i * 6;
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 3;
                triangles[t + 3] = v; triangles[t + 4] = v + 3; triangles[t + 5] = v + 2;
            }
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            GetComponent<MeshFilter>().mesh = mesh;
        }

        /// <summary>풀에서 꺼낼 때마다 호출: 이 사거리·각도로 처음부터 그린다.</summary>
        public void Play(float range, float arcAngle)
        {
            radius = range * radiusScale;
            halfArc = arcAngle * 0.5f;
            age = -lag;
        }

        void Update()
        {
            age += Time.deltaTime;
            float head = EaseOut(age / sweepTime);
            float tail = EaseOut((age - sweepTime * 0.5f) / tailTime);

            for (int i = 0; i <= Segments; i++)
            {
                float u = (float)i / Segments;
                float angle = Mathf.Lerp(-halfArc, halfArc, Mathf.Lerp(tail, head, u));
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                float w = width * Mathf.Sin(Mathf.PI * u);

                vertices[i * 2] = dir * (radius - w) + Vector3.up * height;
                vertices[i * 2 + 1] = dir * radius + Vector3.up * height;
                colors[i * 2] = new Color(1f, 1f, 1f, 0f);
                colors[i * 2 + 1] = new Color(1f, 1f, 1f, u);
            }
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.RecalculateBounds();
        }

        static float EaseOut(float x)
        {
            x = Mathf.Clamp01(x);
            return 1f - (1f - x) * (1f - x) * (1f - x);
        }
    }
}
