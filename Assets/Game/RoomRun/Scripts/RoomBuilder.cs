// Builds the playable shell of a room at runtime: boundary walls and doors for every room, plus a
// temporary floor and props for rooms that have no stage scene yet.
using System.Collections.Generic;
using UnityEngine;

namespace RoomRun
{
    public sealed class Door : MonoBehaviour
    {
        const float SinkDepth = 3.2f;

        public DoorLink Link { get; private set; }
        public DoorSide Side => Link.side;
        public bool IsOpen { get; private set; }

        Transform bars;
        Collider barrier;
        float closedY;
        Vector3 outward, plane;

        public void Init(DoorLink link, Vector3 wallPoint, Transform barsRoot, Collider barrierCollider)
        {
            Link = link;
            bars = barsRoot;
            barrier = barrierCollider;
            closedY = bars.localPosition.y;
            outward = link.side.Dir();
            plane = wallPoint;
            SetOpen(true, instant: true);
        }

        public Vector3 Point => plane;

        public void SetOpen(bool open, bool instant = false)
        {
            IsOpen = open;
            barrier.enabled = !open;
            if (instant) Snap();
        }

        void Snap()
        {
            var p = bars.localPosition;
            p.y = IsOpen ? closedY - SinkDepth : closedY;
            bars.localPosition = p;
        }

        void Update()
        {
            var p = bars.localPosition;
            p.y = Mathf.MoveTowards(p.y, IsOpen ? closedY - SinkDepth : closedY, 14f * Time.deltaTime);
            bars.localPosition = p;

            var run = RunDirector.Instance;
            if (!run || !IsOpen || run.Transitioning || !run.Player) return;
            var offset = run.Player.transform.position - plane;
            offset.y = 0;
            bool past = Vector3.Dot(offset, outward) > 0.15f;
            bool lined = Vector3.Cross(offset, outward).magnitude < 2.6f;
            if (past && lined) run.GoThrough(this);
        }
    }

    public static class RoomBuilder
    {
        public const float DoorGap = 3.6f;
        const float WallHeight = 1.5f, WallThick = 0.8f, ColliderHeight = 8f;

        public static Transform Build(RoomDef room, RoomRunConfig cfg, List<Door> doors, bool placeholder, Vector3 center, Vector2 areaSize)
        {
            var root = new GameObject("Room_" + room.id).transform;
            root.position = center;
            // Stages show their own edges, so only placeholder rooms draw walls. Stages keep the colliders.
            showWalls = placeholder;

            if (placeholder)
            {
                var floorMat = new Material(cfg.floor) { name = "RoomFloor (instance)" };
                floorMat.SetColor("_BaseColor", room.floorColor);
                BuildFloor(root, room, floorMat, cfg);
                Physics.SyncTransforms();
            }

            float hx = areaSize.x / 2f, hz = areaSize.y / 2f;
            foreach (DoorSide side in System.Enum.GetValues(typeof(DoorSide)))
            {
                var link = room.doors.Find(d => d.side == side);
                float half = side == DoorSide.North || side == DoorSide.South ? hx : hz;
                float reach = side == DoorSide.North || side == DoorSide.South ? hz : hx;
                var dir = side.Dir();
                var along = new Vector3(dir.z, 0, dir.x);
                var c = center + dir * reach;

                if (link == null)
                {
                    Wall(root, room, cfg, c, along, half * 2f + WallThick);
                }
                else
                {
                    float seg = half - DoorGap / 2f + WallThick / 2f;
                    float off = DoorGap / 2f + seg / 2f;
                    Wall(root, room, cfg, c + along * off, along, seg);
                    Wall(root, room, cfg, c - along * off, along, seg);
                    // On a stage the boundary can run over a hillside. Bring the gate in to the floor level.
                    var gateAt = c;
                    for (int step = 0; step < 40 && Mathf.Abs(Ground.Sample(gateAt, center.y) - center.y) > 1.5f; step++)
                        gateAt -= dir;
                    doors.Add(BuildDoor(root, room, cfg, link, gateAt, along));
                }
            }

            if (placeholder) BuildPillars(root, room, cfg);
            Physics.SyncTransforms();
            return root;
        }

        // string.GetHashCode changes between runs. Layouts should not.
        static int StableSeed(string s)
        {
            int h = 17;
            foreach (char c in s) h = h * 31 + c;
            return h;
        }

        static void BuildFloor(Transform root, RoomDef room, Material floorMat, RoomRunConfig cfg)
        {
            var floor = new GameObject("Floor", typeof(MeshFilter), typeof(MeshRenderer), typeof(BoxCollider));
            floor.transform.SetParent(root, false);
            floor.transform.localPosition = Vector3.down * 0.5f;
            floor.transform.localScale = new Vector3(room.size.x + 44f, 1f, room.size.y + 34f);
            floor.GetComponent<MeshFilter>().sharedMesh = MeshLib.Cube;
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;

            // Loose flagstones so movement can be read against the ground.
            var slabMat = new Material(cfg.floor) { name = "RoomSlab (instance)" };
            slabMat.SetColor("_BaseColor", room.floorColor * 1.55f);
            var rnd = new System.Random(StableSeed(room.id));
            float hx = room.size.x / 2f, hz = room.size.y / 2f;
            for (float x = -hx + 1f; x < hx; x += 2.1f)
            for (float z = -hz + 1f; z < hz; z += 2.1f)
            {
                if (rnd.NextDouble() < 0.42) continue;
                var s = new GameObject("Slab", typeof(MeshFilter), typeof(MeshRenderer));
                s.transform.SetParent(root, false);
                s.transform.localPosition = new Vector3(x + (float)rnd.NextDouble() * 0.2f, -0.03f, z + (float)rnd.NextDouble() * 0.2f);
                s.transform.localRotation = Quaternion.Euler(0, (float)rnd.NextDouble() * 8f - 4f, 0);
                s.transform.localScale = new Vector3(1.9f, 0.1f, 1.9f);
                s.GetComponent<MeshFilter>().sharedMesh = MeshLib.Cube;
                s.GetComponent<MeshRenderer>().sharedMaterial = slabMat;
            }
        }

        static void Wall(Transform root, RoomDef room, RoomRunConfig cfg, Vector3 c, Vector3 along, float length)
        {
            bool xAxis = Mathf.Abs(along.x) > 0.5f;
            float y = Ground.Sample(c, room.center.y);
            var wall = new GameObject("Wall");
            wall.transform.SetParent(root, false);
            wall.transform.position = new Vector3(c.x, y, c.z);

            var size = xAxis ? new Vector3(length, 1f, WallThick) : new Vector3(WallThick, 1f, length);
            var col = wall.AddComponent<BoxCollider>();
            col.center = Vector3.up * (ColliderHeight / 2f - 2f);
            col.size = new Vector3(size.x, ColliderHeight, size.z);
            wall.layer = Ground.WallLayer;
            if (!showWalls) return;

            var body = MeshLib.Part("Body", MeshLib.Cube, cfg.stone, wall.transform, Vector3.up * (WallHeight / 2f - 1.5f),
                new Vector3(size.x, WallHeight + 3f, size.z));
            body.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            var cap = MeshLib.Part("Cap", MeshLib.Cube, cfg.tile, wall.transform, Vector3.up * (WallHeight + 0.1f),
                new Vector3(size.x + 0.2f, 0.2f, size.z + 0.2f));
            cap.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        static bool showWalls = true;

        static Door BuildDoor(Transform root, RoomDef room, RoomRunConfig cfg, DoorLink link, Vector3 c, Vector3 along)
        {
            float y = Ground.Sample(c, room.center.y);
            var g = new GameObject("Door_" + link.side + "_" + link.targetRoomId);
            g.transform.SetParent(root, false);
            g.transform.position = new Vector3(c.x, y, c.z);
            g.transform.rotation = Quaternion.LookRotation(link.side.Dir());

            // Original red-arrow gate shape from the concept scene.
            float halfGap = DoorGap / 2f;
            foreach (float x in new[] { -halfGap, halfGap })
            {
                var post = MeshLib.Part("Post", MeshLib.Cube, cfg.gate, g.transform, new Vector3(x, 1.45f, 0), new Vector3(0.28f, 3.4f, 0.28f));
                post.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            var beam = MeshLib.Part("Beam", MeshLib.Cube, cfg.gate, g.transform, new Vector3(0, 3.1f, 0), new Vector3(DoorGap + 0.9f, 0.22f, 0.3f));
            beam.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            var bars = new GameObject("Bars").transform;
            bars.SetParent(g.transform, false);
            bars.localPosition = new Vector3(0, 1.2f, 0);
            for (int i = 0; i < 7; i++)
            {
                float x = Mathf.Lerp(-halfGap + 0.35f, halfGap - 0.35f, i / 6f);
                MeshLib.Part("Bar", MeshLib.Cube, cfg.barrier, bars, new Vector3(x, 0, 0), new Vector3(0.11f, 2.4f, 0.11f));
            }

            var barrier = g.AddComponent<BoxCollider>();
            barrier.center = Vector3.up * 2f;
            barrier.size = new Vector3(DoorGap, ColliderHeight, 0.7f);
            g.layer = Ground.WallLayer;

            var door = g.AddComponent<Door>();
            door.Init(link, g.transform.position, bars, barrier);
            return door;
        }

        static void BuildPillars(Transform root, RoomDef room, RoomRunConfig cfg)
        {
            var rnd = new System.Random(StableSeed(room.id) ^ 7);
            float hx = room.size.x / 2f - 4f, hz = room.size.y / 2f - 3.5f;
            var placed = new List<Vector3>();
            int guard = 0;
            while (placed.Count < room.pillarCount && guard++ < 200)
            {
                var p = new Vector3(Mathf.Lerp(-hx, hx, (float)rnd.NextDouble()), 0, Mathf.Lerp(-hz, hz, (float)rnd.NextDouble()));
                // Keep the door lanes and the middle clear.
                if (Mathf.Abs(p.x) < 3.4f || Mathf.Abs(p.z) < 3.4f) continue;
                if (placed.Exists(o => (o - p).sqrMagnitude < 25f)) continue;
                placed.Add(p);
                Lantern(root, cfg, p);
            }
        }

        static void Lantern(Transform root, RoomRunConfig cfg, Vector3 p)
        {
            var t = new GameObject("StoneLantern");
            t.transform.SetParent(root, false);
            t.transform.localPosition = p;
            t.layer = Ground.WallLayer;
            var col = t.AddComponent<BoxCollider>();
            col.center = Vector3.up * 2f;
            col.size = new Vector3(1.1f, 6f, 1.1f);

            void Shadowed(string n, Vector3 lp, Vector3 s)
            {
                var part = MeshLib.Part(n, MeshLib.Cube, cfg.stone, t.transform, lp, s);
                part.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            Shadowed("Base", new Vector3(0, 0.15f, 0), new Vector3(1.1f, 0.3f, 1.1f));
            Shadowed("Pillar", new Vector3(0, 0.85f, 0), new Vector3(0.42f, 1.1f, 0.42f));
            MeshLib.Part("Fire", MeshLib.Cube, cfg.lampFire, t.transform, new Vector3(0, 1.62f, 0), Vector3.one * 0.42f);
            Shadowed("Roof", new Vector3(0, 2.02f, 0), new Vector3(1.05f, 0.2f, 1.05f));

            var lg = new GameObject("Light");
            lg.transform.SetParent(t.transform, false);
            lg.transform.localPosition = new Vector3(0, 1.6f, 0);
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.6f, 0.28f);
            l.intensity = 5f;
            l.range = 7f;
            l.shadows = LightShadows.None;
        }
    }
}
