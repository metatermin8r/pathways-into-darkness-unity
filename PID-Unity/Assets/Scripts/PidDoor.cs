//A single door, PidWorld builds these at level load

using UnityEngine;

namespace Pid
{
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class PidDoor : MonoBehaviour
    {
        public const int Closed = 1024;

        public const int BlockThreshold = 512;

        public int sectorX, sectorY;
        public int doorIndex;
        public int textureId;

        //Handles which way a door slides and which edge it hides in
        public int direction;

        public int position = Closed;

        //0 idle, 1 opening, 2 closing
        public int command;

        Mesh mesh;
        BoxCollider box;
        int builtPosition = -1;

        public bool SlidesY => (direction & 1) != 0;
        public bool AnchorHigh => ((direction >> 1) & 1) != 0;

        //Speeds come from a table in the binary, keyed by the door's texture for some reason. Incomprehensible, thank you Jason Jones.
        public static int RateFor(int texture)
        {
            switch (texture) //Some unknown
            {
                case 0:
                case 1: return 12;
                case 3:
                case 4:
                case 5: return 17;
                case 6: return 25;
                default: return 12;
            }
        }

        //How fast the door moves in raw units
        public int rate = 12;

        public int Rate => rate;

        public float TraversalSeconds => Closed / (Rate * 60f);

        //Half-open still blocks the whole doorway, so you can't squeeze past on the open side
        public bool Blocks => position > BlockThreshold || command == 2;

        public void Open() { command = 1; }
        public void Close() { command = 2; }

        public void Configure(int sx, int sy, int index, int texture, int dir,
                       int initialPosition, int initialCommand, int tickRate)
        {
            sectorX = sx; sectorY = sy;
            doorIndex = index; textureId = texture; direction = dir;
            rate = tickRate;
            position = Mathf.Clamp(initialPosition, 0, Closed);
            command = initialCommand;

            transform.localPosition = new Vector3(sx * PidConst.SectorSize, 0f,
                                                  -sy * PidConst.SectorSize);
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            mesh = new Mesh { name = $"Door_{index}_({sx},{sy})" };
            GetComponent<MeshFilter>().sharedMesh = mesh;

            box = GetComponent<BoxCollider>();
            if (box == null) box = gameObject.AddComponent<BoxCollider>();
            float s = PidConst.SectorSize;
            //Cell spans 0..S in x, 0..-S in z.
            box.center = new Vector3(s * 0.5f, PidConst.WallHeight * 0.5f, -s * 0.5f);
            box.size = new Vector3(s, PidConst.WallHeight, s);

            builtPosition = -1;
            Rebuild();
        }

        void Update()
        {
            //Only commands 1 and 2 do anything, command 0 stops the door
            if (command == 1 || command == 2)
            {
                int step = Mathf.Max(1, Mathf.RoundToInt(Rate * Time.deltaTime * 60f));
                if (command == 1)
                {
                    position -= step;
                    if (position <= 0) { position = 0; command = 0; }
                }
                else
                {
                    position += step;
                    if (position >= Closed) { position = Closed; command = 0; }
                }
            }

            if (position != builtPosition) Rebuild();
            if (box != null) box.enabled = Blocks;
        }

        static readonly System.Collections.Generic.List<Vector3> Verts =
            new System.Collections.Generic.List<Vector3>(12);
        static readonly System.Collections.Generic.List<Vector2> Uvs =
            new System.Collections.Generic.List<Vector2>(12);

        //The flat faces and the leading edge use different artwork, so we need two submeshes
        static readonly System.Collections.Generic.List<int> TrisFaces =
            new System.Collections.Generic.List<int>(24);
        static readonly System.Collections.Generic.List<int> TrisCap =
            new System.Collections.Generic.List<int>(12);

        void Rebuild()
        {
            builtPosition = position;
            if (mesh == null) return;

            mesh.Clear();
            mesh.subMeshCount = 2;

            //Fully open means we just cull the door because its in a wall
            if (position <= 0) return;

            const float Cell = 1024f;
            float u = PidConst.SectorSize / Cell;
            float h = PidConst.WallHeight;

            float fixedRaw = AnchorHigh ? Cell : 0f;
            float moveRaw = AnchorHigh ? Cell - position : position;

            const float NearRaw = 256f;
            const float FarRaw = 768f;

            Verts.Clear(); Uvs.Clear(); TrisFaces.Clear(); TrisCap.Clear();

            Vector3 P(float alongRaw, float acrossRaw, float y)
            {
                float xr = SlidesY ? acrossRaw : alongRaw;
                float yr = SlidesY ? alongRaw : acrossRaw;
                return new Vector3(xr * u, y, -yr * u);
            }

            void Quad(System.Collections.Generic.List<int> t,
                      Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                      Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                int i = Verts.Count;
                Verts.Add(a); Uvs.Add(ua);
                Verts.Add(b); Uvs.Add(ub);
                Verts.Add(c); Uvs.Add(uc);
                Verts.Add(d); Uvs.Add(ud);
                t.Add(i); t.Add(i + 2); t.Add(i + 1);
                t.Add(i); t.Add(i + 3); t.Add(i + 2);
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
                t.Add(i); t.Add(i + 2); t.Add(i + 3);
            }

            //Pins the texture to the world, so it slides past rather than stretching as the door opens
            bool bitsDiffer = SlidesY != AnchorHigh;
            float t0 = bitsDiffer ? (Cell - position) / Cell : 0f;
            float t1 = bitsDiffer ? 1f : position / Cell;

            Quad(TrisFaces,
                 P(fixedRaw, NearRaw, 0f), P(moveRaw, NearRaw, 0f),
                 P(moveRaw, NearRaw, h), P(fixedRaw, NearRaw, h),
                 new Vector2(t0, 0), new Vector2(t1, 0),
                 new Vector2(t1, 1), new Vector2(t0, 1));

            Quad(TrisFaces,
                 P(fixedRaw, FarRaw, 0f), P(moveRaw, FarRaw, 0f),
                 P(moveRaw, FarRaw, h), P(fixedRaw, FarRaw, h),
                 new Vector2(t0, 0), new Vector2(t1, 0),
                 new Vector2(t1, 1), new Vector2(t0, 1));

            if (position < Closed)
            {
                Quad(TrisCap,
                     P(moveRaw, NearRaw, 0f), P(moveRaw, FarRaw, 0f),
                     P(moveRaw, FarRaw, h), P(moveRaw, NearRaw, h),
                     new Vector2(0, 0), new Vector2(1, 0),
                     new Vector2(1, 1), new Vector2(0, 1));
            }

            mesh.SetVertices(Verts);
            mesh.SetUVs(0, Uvs);
            mesh.SetTriangles(TrisFaces, 0, true);
            mesh.SetTriangles(TrisCap, 1, true);
            mesh.RecalculateNormals();
        }
    }
}