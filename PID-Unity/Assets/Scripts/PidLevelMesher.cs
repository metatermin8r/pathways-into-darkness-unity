// PidLevelMesher.cs
// Pathways Into Darkness -> Unity. Milestone 1: flat-shaded walkable level.
//
// Deserializes pid_level_v1 (pid-re/reference/export/L##.json) with Newtonsoft.
// Unity's JsonUtility cannot read this format: texture_list carries nullable ints
// (shape_id, variation) and the schema nests objects several levels deep.
// Package Manager -> com.unity.nuget.newtonsoft-json
//
// COORDINATE MAPPING
//   Game grid is 32x32, index = y*32 + x, x rightward, y DOWNWARD (southward).
//   Unity Z increases north, so game y maps to NEGATIVE Z.
//   Sector (x,y) occupies world X in [x*S, (x+1)*S], Z in [-(y+1)*S, -y*S].
//   Its NORTH edge (game -Y) is the Z = -y*S plane.
//   Its WEST  edge (game -X) is the X =  x*S plane.
//   If the level renders mirrored or rotated, it is the Z sign.
//
// EDGE OWNERSHIP
//   walls[0] (slot "wall_y") is the sector's OWN north edge.
//   walls[1] (slot "wall_x") is the sector's OWN west edge.
//   Every edge in the grid is described exactly once, by the cell to its south or
//   east. Emitting all four edges per cell would double every interior wall.
//
// BLOCKING
//   Read from wall.blocks_movement, never inferred here. The export sets it from
//   its own movement_rule ("{32}" on this level: 679 type-32 walls true, all 329
//   short walls and corners false). A parser change should not need an engine
//   change - that boundary is the point of the two repos.

using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace Pid
{
    public static class PidConst
    {
        public const int Grid = 32;
        public const float SectorSize = 3.0f;   // metres
        public const float WallHeight = 2.6f;   // just under the ~2.7 m inter-level spacing

        // sector type
        public const int TypeVoid = 0, TypeNormal = 1, TypeDoor = 2, TypeChangeLevel = 3,
                         TypeDoorTrigger = 4, TypeSecretDoor = 5, TypeCorpse = 6,
                         TypePillar = 7, TypeOtherTrigger = 8, TypeSave = 9;

        // TEMPORARY, MILESTONE 1 ONLY.
        // 64/96/128 are named wall_short_low / wall_short_high / wall_short_both, which
        // SUGGESTS a low band, a high band, and both-with-a-gap. That is inference from
        // naming, not verified against bytes. Ground Floor has 189 short walls, most of
        // them on the world boundary, so guessing a vertical extent now would punch
        // visible holes during the exact test we are running. Full height until .256
        // lands and we can see what the bands actually look like.
        public static float HeightOf(int wallType) => WallHeight;
    }

    // ---- pid_level_v1 schema ----------------------------------------------------

    public class PidWall
    {
        public int index;
        public string slot;          // "wall_y" (north), "wall_x" (west), then 4 corners
        public int type;          // 0, 1, 32, 33, 64, 96, 128, 160
        public string type_name;
        public int texture;
        public bool blocks_movement;
    }

    public class PidSector
    {
        public int index;
        public int x, y;
        public int type;
        public string type_name;
        public int type_addl;
        public int item;
        public List<PidWall> walls;   // 6

        public bool IsVoid => type == PidConst.TypeVoid;
        public bool IsPillar => type == PidConst.TypePillar;
    }

    public class PidTextureRef
    {
        public int raw;
        public int? shape_id;     // null where raw == -1. This is why JsonUtility fails.
        public int? variation;
    }

    /// door_list entry. `direction` names the edge the panel RETRACTS INTO, not the
    /// plane the panel occupies. On Ground Floor all three doors sit in a one-tile gap
    /// with two void sides; the void-facing edges carry the 128/13 jambs, and the edges
    /// you walk through carry wall_type 0. The panel itself has no map geometry - it is
    /// built from `texture` and spans the tile perpendicular to the travel axis.
    public class PidDoorDef
    {
        public int index;
        public int x, y;
        public int direction;        // 0 x_negative, 1 y_negative, 2 x_positive, 3 y_positive
        public string direction_name;
        public int texture;
        public bool referenced_by_type2;
    }

    /// level_change_list entry. dest_x/dest_y are coordinates on dest_level - the export
    /// has already resolved the cross-level scan, so these are where the player lands.
    public class PidLevelChange
    {
        public int index;
        public int source_level;
        public string source_name;
        public int dest_level;
        public int dest_x, dest_y;
        public int type;             // 0 upward, 1 downward, 2 secret_downward, 3 secret_upward
        public string type_name;
        public bool live;
        public bool empty;
        public bool referenced_by_type3;
    }

    /// Where the player lands when entering this level from somewhere else. Arrival
    /// coordinates live in the SOURCE level's level_change_list, so the export has
    /// already done the cross-level scan; these are resolved entries pointing here.
    public class PidArrival
    {
        public int x, y;
        public int from_level;
        public string from_name;
        public int change_type;
        public string change_type_name;
        public int list_index;
    }

    public class PidLevel
    {
        public string format;
        public string movement_rule;
        public int grid;
        public int record_size;
        public int level_number;
        public string name;
        public int height10;
        public List<PidTextureRef> texture_list;
        public List<PidSector> sectors;      // 1024, row-major
        public List<PidArrival> arrivals;
        public List<PidDoorDef> doors;        // 15 slots, most unreferenced
        public List<PidLevelChange> level_changes;  // 20 slots, unused have type -1

        public bool InBounds(int x, int y) =>
            x >= 0 && y >= 0 && x < PidConst.Grid && y < PidConst.Grid;

        public PidSector At(int x, int y) =>
            InBounds(x, y) ? sectors[y * PidConst.Grid + x] : null;

        /// Void and Pillar are movement-equivalent: neither has a floor you can stand on.
        /// They differ only in which side their faces are seen from. Collapsing them into
        /// one predicate removes every special case in the emission loop but one.
        /// Off-grid counts as Void - the grid edge is the edge of the world.
        public bool IsSolid(int x, int y) { var s = At(x, y); return s == null || s.IsVoid || s.IsPillar; }

        public int WallType(int x, int y, int slot)
        {
            var s = At(x, y);
            return s == null ? 0 : s.walls[slot].type;
        }

        public bool Blocks(int x, int y, int slot)
        {
            var s = At(x, y);
            return s != null && s.walls[slot].blocks_movement;
        }
    }

    // ---- mesh assembly ----------------------------------------------------------

    public class MeshAccum
    {
        public readonly List<Vector3> verts = new List<Vector3>();
        public readonly List<Vector3> norms = new List<Vector3>();
        public readonly List<int> tris = new List<int>();
        public int quadCount;

        /// (a,b,c,d) is front-facing from the side its normal points toward.
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i = verts.Count;
            Vector3 nrm = Vector3.Cross(b - a, c - a).normalized;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            norms.Add(nrm); norms.Add(nrm); norms.Add(nrm); norms.Add(nrm);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
            quadCount++;
        }

        /// Two coplanar quads with opposite winding. Backface culling means exactly one
        /// is ever drawn, so they do not z-fight, and each side gets a correct flat
        /// normal. Levels 7-15 have hundreds of interior walls seen from both sides.
        public void QuadBoth(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Quad(a, b, c, d);
            Quad(d, c, b, a);
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(verts);
            m.SetNormals(norms);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }
    }

    public static class PidLevelMesher
    {
        const float S = PidConst.SectorSize;

        static float XW(int x) => x * S;      // west plane of column x
        static float ZN(int y) => -y * S;     // north plane of row y

        public static Vector3 SectorCentre(int x, int y, float height = 0f) =>
            new Vector3(XW(x) + S * 0.5f, height, ZN(y) - S * 0.5f);

        public static PidLevel Parse(string json) => JsonConvert.DeserializeObject<PidLevel>(json);

        public struct Result
        {
            public Mesh render, collision;
            public int nonVoid, pillars, walkable;
            public int floorQuads, wallQuads, colliderQuads, synthesizedPillarFaces, skippedSolidSolid;
            public int components, largestComponent;
            public List<string> unreachable;   // saves / ladders outside the main component
        }

        public static Result Build(PidLevel lvl)
        {
            var vis = new MeshAccum();
            var col = new MeshAccum();
            int nonVoid = 0, pillars = 0, walkable = 0;
            int floors = 0, walls = 0, colliders = 0, synth = 0, skipped = 0;

            for (int y = 0; y < PidConst.Grid; y++)
                for (int x = 0; x < PidConst.Grid; x++)
                {
                    var s = lvl.At(x, y);
                    if (!s.IsVoid) nonVoid++;
                    if (s.IsPillar) pillars++;

                    // ---- floor + ceiling: WALKABLE cells only ----
                    // Floors belong to the walkable region; faces belong to individual edges.
                    // That asymmetry is the whole trick.
                    if (!lvl.IsSolid(x, y))
                    {
                        walkable++;
                        float x0 = XW(x), x1 = XW(x + 1), zn = ZN(y), zs = ZN(y + 1);

                        // Floor goes into BOTH meshes. A non-convex MeshCollider is one-sided
                        // and follows triangle winding, so this must face +Y or the player
                        // falls straight through it.
                        var f0 = new Vector3(x0, 0f, zn); var f1 = new Vector3(x1, 0f, zn);
                        var f2 = new Vector3(x1, 0f, zs); var f3 = new Vector3(x0, 0f, zs);
                        vis.Quad(f0, f1, f2, f3);
                        col.Quad(f0, f1, f2, f3);
                        floors++;
                        colliders++;

                        // Ceiling is render-only. PID has no jump and no crouch, so nothing can
                        // ever reach it; a collider there would only cost triangles.
                        float h = PidConst.WallHeight;
                        vis.Quad(new Vector3(x0, h, zs), new Vector3(x1, h, zs),
                                 new Vector3(x1, h, zn), new Vector3(x0, h, zn));      // -Y
                    }

                    EmitEdge(lvl, vis, col, x, y, 0, ref walls, ref colliders, ref synth, ref skipped);
                    EmitEdge(lvl, vis, col, x, y, 1, ref walls, ref colliders, ref synth, ref skipped);
                }

            var r = new Result
            {
                render = vis.ToMesh($"PID_L{lvl.level_number:D2}_render"),
                collision = col.ToMesh($"PID_L{lvl.level_number:D2}_collision"),
                nonVoid = nonVoid,
                pillars = pillars,
                walkable = walkable,
                floorQuads = floors,
                wallQuads = walls,
                colliderQuads = colliders,
                synthesizedPillarFaces = synth,
                skippedSolidSolid = skipped
            };
            Connectivity(lvl, ref r);
            return r;
        }

        static void EmitEdge(PidLevel lvl, MeshAccum vis, MeshAccum col, int x, int y, int slot,
                             ref int walls, ref int colliders, ref int synth, ref int skipped)
        {
            int nx = slot == 0 ? x : x - 1;
            int ny = slot == 0 ? y - 1 : y;

            bool aSolid = lvl.IsSolid(x, y);
            bool bSolid = lvl.IsSolid(nx, ny);
            int wt = lvl.WallType(x, y, slot);

            // CASE 1: both sides solid. Nobody can stand on either side, so the face is
            // never seen. Void sectors carry real wall data - 407 of them on Ground Floor
            // - but only the ones touching the walkable region are visible. Without this
            // clause Ground Floor emits 673 extra wall quads of unreachable
            // authoring-tool residue.
            if (aSolid && bSolid)
            {
                if (wt != 0) skipped++;
                return;
            }

            // CASE 2: exactly one side solid. This is a boundary of the walkable region.
            // Geometry MUST exist here or the player sees out of the world, and it is
            // always a collider.
            //
            // For Void boundaries the map always supplies a wall: the census found zero
            // open-to-void cases across all 25 levels, all 25,600 sectors. The boundary
            // is sealed everywhere by the data itself.
            //
            // For PILLAR boundaries on the type-32 levels it does NOT. Every pillar-to-
            // walkable edge on levels 0-6 and 16-24 carries wall type 0 - 1,541 game-wide,
            // 49 on Ground Floor. Those faces have to be synthesized or every pillar is a
            // hole you can see through. On the type-33 levels the map does supply them, as
            // type 33: draws, does not collide - exactly a pillar side. Same authoring
            // split as 32 versus 33.
            if (aSolid != bSolid)
            {
                if (wt == 0) synth++;
                EmitQuad(vis, x, y, slot, PidConst.HeightOf(wt));
                EmitQuad(col, x, y, slot, PidConst.WallHeight);
                walls++; colliders++;
                return;
            }

            // CASE 3: both sides walkable. Draw if there is a wall; collide only if the
            // export says so. Type 33 renders and never collides - that finding is
            // load-bearing, and the arithmetic backs it: level 9 has 415 walkable tiles
            // and only 91 open interior edges, so treating 33 as solid gives at least
            // 415-91 = 324 fragments, which is exactly what the flood reports.
            if (wt != 0) { EmitQuad(vis, x, y, slot, PidConst.HeightOf(wt)); walls++; }
            if (lvl.Blocks(x, y, slot)) { EmitQuad(col, x, y, slot, PidConst.WallHeight); colliders++; }
        }

        static void EmitQuad(MeshAccum m, int x, int y, int slot, float h)
        {
            if (slot == 0)
            {
                // North edge lies in the Z = ZN(y) plane. Normal of (0,1,2,3) points +Z.
                float x0 = XW(x), x1 = XW(x + 1), z = ZN(y);
                m.QuadBoth(new Vector3(x0, 0f, z), new Vector3(x1, 0f, z),
                           new Vector3(x1, h, z), new Vector3(x0, h, z));
            }
            else
            {
                // West edge lies in the X = XW(x) plane. Normal of (0,1,2,3) points +X.
                float xw = XW(x), zn = ZN(y), zs = ZN(y + 1);
                m.QuadBoth(new Vector3(xw, 0f, zn), new Vector3(xw, 0f, zs),
                           new Vector3(xw, h, zs), new Vector3(xw, h, zn));
            }
        }

        /// Flood fill over walkable cells using the same blocking rule the colliders use.
        /// A load-time assertion, not a feature: if the milestone's saves and ladders are
        /// not all in one component, the walk test cannot pass and there is no point
        /// launching it. This will earn its keep on levels 4 and 20-23, which are
        /// genuinely split into void-separated regions linked only by transitions.
        static void Connectivity(PidLevel lvl, ref Result r)
        {
            int n = PidConst.Grid * PidConst.Grid;
            var comp = new int[n];
            for (int i = 0; i < n; i++) comp[i] = -1;
            int id = 0, largest = 0, largestId = -1;
            var stack = new Stack<int>();

            for (int start = 0; start < n; start++)
            {
                int sx = start % PidConst.Grid, sy = start / PidConst.Grid;
                if (lvl.IsSolid(sx, sy) || comp[start] != -1) continue;

                int size = 0;
                stack.Push(start);
                comp[start] = id;
                while (stack.Count > 0)
                {
                    int cur = stack.Pop(); size++;
                    int cx = cur % PidConst.Grid, cy = cur / PidConst.Grid;
                    TryStep(lvl, comp, stack, id, cx, cy, cx, cy - 1);
                    TryStep(lvl, comp, stack, id, cx, cy, cx, cy + 1);
                    TryStep(lvl, comp, stack, id, cx, cy, cx - 1, cy);
                    TryStep(lvl, comp, stack, id, cx, cy, cx + 1, cy);
                }
                if (size > largest) { largest = size; largestId = id; }
                id++;
            }

            r.components = id;
            r.largestComponent = largest;
            r.unreachable = new List<string>();

            // A level being split into several walkable regions is NORMAL and intended.
            // Level 23 has four sealed 13-tile pods, each holding four ladders, on a level
            // called "Where Only Fools Dare Tread". Level 20 has an isolated 3x3 room that
            // two teleporters on Happy Happy, Carnage Carnage drop into - a designed trap.
            // Across the game 51 saves and ladders sit outside their level's largest
            // component, and none of that is a bug.
            //
            // So the question is not "is everything in one piece", it is "can every piece
            // be entered". A component containing no arrival coordinate has no way in.
            var withArrival = new HashSet<int>();
            if (lvl.arrivals != null)
                foreach (var a in lvl.arrivals)
                {
                    if (!lvl.InBounds(a.x, a.y)) continue;
                    int ai = a.y * PidConst.Grid + a.x;
                    if (comp[ai] >= 0) withArrival.Add(comp[ai]);
                }

            var sizes = new int[id];
            for (int i = 0; i < n; i++) if (comp[i] >= 0) sizes[comp[i]]++;

            for (int c = 0; c < id; c++)
            {
                if (withArrival.Contains(c)) continue;
                if (c == largestId) continue;   // entered by whatever route brought you here

                int saves = 0, ladders = 0, sx = -1, sy = -1;
                for (int i = 0; i < n; i++)
                {
                    if (comp[i] != c) continue;
                    var s = lvl.At(i % PidConst.Grid, i / PidConst.Grid);
                    if (sx < 0) { sx = s.x; sy = s.y; }
                    if (s.type == PidConst.TypeSave) saves++;
                    if (s.type == PidConst.TypeChangeLevel) ladders++;
                }
                r.unreachable.Add($"component of {sizes[c]} tiles near ({sx},{sy}), " +
                                  $"no arrival, {ladders} ladder(s) {saves} save(s)");
            }
        }

        static void TryStep(PidLevel lvl, int[] comp, Stack<int> stack, int id,
                            int ax, int ay, int bx, int by)
        {
            if (!lvl.InBounds(bx, by) || lvl.IsSolid(bx, by)) return;
            int bi = by * PidConst.Grid + bx;
            if (comp[bi] != -1) return;

            // The blocking edge is always described by the cell to the south or east.
            int ox = bx > ax ? bx : ax;
            int oy = by > ay ? by : ay;
            int slot = (ay != by) ? 0 : 1;
            if (lvl.Blocks(ox, oy, slot)) return;

            comp[bi] = id;
            stack.Push(bi);
        }
    }
}