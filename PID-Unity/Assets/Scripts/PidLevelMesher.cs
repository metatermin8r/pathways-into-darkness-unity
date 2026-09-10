//Turns a level's sector grid (from a JSON file) into Unity meshes.
//Floors and ceilings are built for collision only, because the original doesn't draw either

using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Pid
{
    public static class PidConst
    {
        public const int Grid = 32;
        public const float SectorSize = 3.0f;

        //1024 raw units to a sector
        public const int RawUnitsPerSector = 1024;

        //In PID the floor sits 614 below the eye and the ceiling 409 above, so a wall is 1023 tall and you're standing at 60% of its height
        public const int EyeToFloorRaw = 614;
        public const int EyeToCeilingRaw = 409;
        public const int CeilingRawUnits = EyeToFloorRaw + EyeToCeilingRaw;   //Should always be 1023

        //Wall tiles are square, so wall height is sector size
        public const float WallHeight =
            SectorSize * CeilingRawUnits / (float)RawUnitsPerSector;

        //One conversion for everything so the scales can't drift apart
        public const float RawToMetres = SectorSize / RawUnitsPerSector;

        //Eye height never changes
        public const float EyeHeight = SectorSize * EyeToFloorRaw / (float)RawUnitsPerSector;

        //Fixed, works out to a 4:3 view no matter what size the window is
        public const float VerticalFovDegrees = 61.927513f;

        public const int TypeVoid = 0, TypeNormal = 1, TypeDoor = 2, TypeChangeLevel = 3,
                         TypeDoorTrigger = 4, TypeSecretDoor = 5, TypeCorpse = 6,
                         TypePillar = 7, TypeOtherTrigger = 8, TypeSave = 9;

        //Walls and objects pack the same way, only the tag differs
        public static int Tag(int word) => (word >> 13) & 7;
        public static int S1Index(int word) => word & 0x7F;
        public static int Selector(int word) => ((word >> 7) & 0x3F) + (Tag(word) == 6 ? 0 : 64);
        //Cache slot N is resource N+128!!!
        public static int Resource(int word) => Selector(word) + 128;
    }

    //Points a resource and tile index at the PNG to draw and how big it is

    public class PidTile
    {
        public int s1_index;
        public int cls;
        public int width;
        public int height;

        //R8 index map: Pixels are palette indices, so one file covers every colour variation, and the LUT does the colouring
        public string png;

        //Sprite size in raw units, zero on wall tiles
        public int world_w;
        public int world_h;

        //How far off the floor the sprite sits, probably not needed since we now have floor/ceiling rendering (or lack there off) solved
        public int lift;

        //Palette is ignored, kept so call sites don't have to change
        public string Png(int palette) => png;
    }

    public class PidDoorRate
    {
        //Which tiles a door uses, looked up by its texture number
        public int texture;
        public int face_s1;
        public int cap_s1;

        //Raw units per tick (used for movement math)
        public int rate;
    }
    public class PidResourceTiles
    {
        public int resource;
        public int table_count;   //colour tables this resource carries
        public int full_height;   //manifest's value
        public List<PidTile> tiles;

        Dictionary<int, PidTile> byIndex;
        int fullH = -1;

        //Take the tallest class-1 tile, not the first
        public int FullHeight
        {
            get
            {
                if (fullH < 0)
                {
                    fullH = 0;
                    if (tiles != null)
                        foreach (var t in tiles)
                            if (t.cls == 1 && t.height > fullH) fullH = t.height;
                    if (fullH == 0) fullH = full_height;   //sprite-only resources
                }
                return fullH;
            }
        }

        public PidTile Get(int s1Index)
        {
            if (byIndex == null)
            {
                byIndex = new Dictionary<int, PidTile>();
                if (tiles != null)
                    foreach (var t in tiles) byIndex[t.s1_index] = t;
            }
            return byIndex.TryGetValue(s1Index, out var tile) ? tile : null;
        }
    }

    public class PidLevelTextures
    {
        public int wall_resource;
        public int wall_variation;
    }

    public class PidTextureManifest
    {
        public List<PidResourceTiles> resources; //Which wall resource and colour table each level uses
        public Dictionary<string, PidLevelTextures> levels;

        Dictionary<int, PidResourceTiles> byId;

        public PidLevelTextures ForLevel(int n) =>
            levels != null && levels.TryGetValue(n.ToString(), out var l) ? l : null;

        //Multiple levels share the same texture resources, colored differently with the LUTs
        public int VariationFor(int level) => ForLevel(level)?.wall_variation ?? 0;

        public PidResourceTiles Get(int resource)
        {
            if (byId == null)
            {
                byId = new Dictionary<int, PidResourceTiles>();
                if (resources != null)
                    foreach (var r in resources) byId[r.resource] = r;
            }
            return byId.TryGetValue(resource, out var res) ? res : null;
        }

        public static PidTextureManifest Parse(string json) =>
            JsonConvert.DeserializeObject<PidTextureManifest>(json);

        public List<PidDoorRate> door_rates;

        public PidDoorRate DoorRate(int texture)
        {
            if (door_rates == null) return null;
            foreach (var r in door_rates) if (r.texture == texture) return r;
            return null;
        }
    }

    //**********LEVEL SCHEMA**********

    public class PidWall
    {
        public int index;
        public string slot;
        public int type; //Descriptor's high byte
        public string type_name;
        public int texture;       //Descriptor's low byte
        public bool blocks_movement;

        public int Word => ((type & 0xFF) << 8) | (texture & 0xFF);
    }

    public class PidSector
    {
        public int index;
        public int x, y;
        public int type;
        public string type_name;
        public int type_addl;
        public int item;
        public List<PidWall> walls;

        public bool IsVoid => type == PidConst.TypeVoid;
        public bool IsPillar => type == PidConst.TypePillar;
    }

    public class PidTextureRef { public int raw; public int? shape_id; public int? variation; }

    public class PidArrival
    {
        public int x, y, from_level, change_type, list_index;
        public string from_name, change_type_name;
    }

    public class PidDoorDef
    {
        public int index, x, y, direction, texture;
        public string direction_name;
        public bool referenced_by_type2;
    }

    public class PidLevelChange
    {
        public int index, source_level, dest_level, dest_x, dest_y, type;
        public string source_name, type_name;
        public bool live, empty, referenced_by_type3;
    }

    public class PidLevel
    {
        public string format, movement_rule, name;
        public int grid, record_size, level_number, height10;
        public List<PidTextureRef> texture_list;
        public List<PidSector> sectors;
        public List<PidArrival> arrivals;
        public List<PidDoorDef> doors;
        public List<PidLevelChange> level_changes;

        public bool InBounds(int x, int y) =>
            x >= 0 && y >= 0 && x < PidConst.Grid && y < PidConst.Grid;

        public PidSector At(int x, int y) =>
            InBounds(x, y) ? sectors[y * PidConst.Grid + x] : null;

        //Only Void has no floor (everything off the level grid counts as void)
        //PID draws floor, ceiling, and the neighbouring walls around things like pillars, with the pillar standing in front as a sprite
        public bool IsVoidAt(int x, int y) { var s = At(x, y); return s == null || s.IsVoid; }

        //Pillars block like walls, despite not being walls or void
        public bool IsBlocking(int x, int y) { var s = At(x, y); return s == null || s.IsVoid || s.IsPillar; }

        public int Word(int x, int y, int slot)
        {
            var s = At(x, y);
            return s == null ? 0 : s.walls[slot].Word;
        }

        public bool Blocks(int x, int y, int slot)
        {
            var s = At(x, y);
            return s != null && s.walls[slot].blocks_movement;
        }
    }

    //**********MESH ASSEMBLY**********

    public class MeshAccum
    {
        public readonly List<Vector3> verts = new List<Vector3>();
        public readonly List<Vector3> norms = new List<Vector3>();
        public readonly List<Vector2> uvs = new List<Vector2>();
        public readonly List<int> tris = new List<int>();
        public int quadCount;

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                         Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            int i = verts.Count;
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            norms.Add(n); norms.Add(n); norms.Add(n); norms.Add(n);
            uvs.Add(ua); uvs.Add(ub); uvs.Add(uc); uvs.Add(ud);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
            quadCount++;
        }

        //Same quad twice so it's visible from either side (we do some culling to stop Z fighting issues)
        public void QuadBoth(Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                             Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            Quad(a, b, c, d, ua, ub, uc, ud);
            Quad(d, c, b, a, ud, uc, ub, ua);
        }
    }

    public static class PidLevelMesher
    {
        const float S = PidConst.SectorSize;

        static float XW(int x) => x * S;
        static float ZN(int y) => -y * S;

        public static Vector3 SectorCentre(int x, int y, float height = 0f) =>
            new Vector3(XW(x) + S * 0.5f, height, ZN(y) - S * 0.5f);

        public static PidLevel Parse(string json) => JsonConvert.DeserializeObject<PidLevel>(json);

        public struct Result
        {
            public Mesh render;
            public Mesh collision;
            public List<int> submeshTileKey; //Submesh 0 is floors, 1 is ceilings (now unused), tiles start at 2
            public Dictionary<int, Mesh> markers;
            public int nonVoid, pillars, walkable;
            public int floorQuads, wallQuads, colliderQuads, pillarBoundaryColliders;
            public int skippedSolidSolid, skippedTagZero, missingTiles;
            public int components, largestComponent;
            public List<string> unreachable;
        }

        static readonly int[] MarkedTypes =
        {
            PidConst.TypeDoor, PidConst.TypeChangeLevel, PidConst.TypeDoorTrigger,
            PidConst.TypeSecretDoor, PidConst.TypeCorpse, PidConst.TypeOtherTrigger,
            PidConst.TypeSave
        };

        public static Result Build(PidLevel lvl, PidTextureManifest manifest)
        {
            var floors = new MeshAccum();
            var ceilings = new MeshAccum();
            var col = new MeshAccum();
            var byTile = new Dictionary<int, MeshAccum>(); //tile key for geometry
            var marks = new Dictionary<int, MeshAccum>();

            int nonVoid = 0, pillars = 0, walkable = 0;
            int floorQuads = 0, wallQuads = 0, colliders = 0;
            int pillarColliders = 0, skippedSolid = 0, skippedTag0 = 0, missing = 0;

            //Which colour table this level's walls use
            int palette = manifest?.VariationFor(lvl.level_number) ?? 0;

            for (int y = 0; y < PidConst.Grid; y++)
                for (int x = 0; x < PidConst.Grid; x++)
                {
                    var s = lvl.At(x, y);
                    if (!s.IsVoid) nonVoid++;
                    if (s.IsPillar) pillars++;

                    if (!lvl.IsVoidAt(x, y))
                    {
                        walkable++;
                        float x0 = XW(x), x1 = XW(x + 1), zn = ZN(y), zs = ZN(y + 1);
                        var f0 = new Vector3(x0, 0f, zn); var f1 = new Vector3(x1, 0f, zn);
                        var f2 = new Vector3(x1, 0f, zs); var f3 = new Vector3(x0, 0f, zs);

                        if (drawFloors)
                            floors.Quad(f0, f1, f2, f3, V(0, 0), V(1, 0), V(1, 1), V(0, 1));
                        col.Quad(f0, f1, f2, f3, V(0, 0), V(1, 0), V(1, 1), V(0, 1));
                        floorQuads++; colliders++;

                        //Ceiling is render-only
                        if (drawCeilings)
                        {
                            float h = PidConst.WallHeight;
                            ceilings.Quad(new Vector3(x0, h, zs), new Vector3(x1, h, zs),
                                          new Vector3(x1, h, zn), new Vector3(x0, h, zn),
                                          V(0, 0), V(1, 0), V(1, 1), V(0, 1));
                        }

                        if (System.Array.IndexOf(MarkedTypes, s.type) >= 0)
                        {
                            if (!marks.TryGetValue(s.type, out var acc))
                                marks[s.type] = acc = new MeshAccum();
                            const float lift = 0.02f;
                            acc.Quad(f0 + Vector3.up * lift, f1 + Vector3.up * lift,
                                     f2 + Vector3.up * lift, f3 + Vector3.up * lift,
                                     V(0, 0), V(1, 0), V(1, 1), V(0, 1));
                        }
                    }

                    EmitEdge(lvl, manifest, palette, byTile, col, x, y, 0,
                             ref wallQuads, ref colliders, ref pillarColliders,
                             ref skippedSolid, ref skippedTag0, ref missing);
                    EmitEdge(lvl, manifest, palette, byTile, col, x, y, 1,
                             ref wallQuads, ref colliders, ref pillarColliders,
                             ref skippedSolid, ref skippedTag0, ref missing);

                    //Slots 2-5 are the four corner diagonals, emitted from the CURRENT cell before the void skip, so void cells still
                    //contribute chamfers
                    for (int slot = 2; slot <= 5; slot++)
                        EmitCorner(lvl, manifest, palette, byTile, x, y, slot,
                                   ref wallQuads, ref missing);
                }

            var r = new Result
            {
                collision = ToMesh(col, $"PID_L{lvl.level_number:D2}_collision"),
                markers = BuildMarkers(marks, lvl.level_number),
                nonVoid = nonVoid,
                pillars = pillars,
                walkable = walkable,
                floorQuads = floorQuads,
                wallQuads = wallQuads,
                colliderQuads = colliders,
                pillarBoundaryColliders = pillarColliders,
                skippedSolidSolid = skippedSolid,
                skippedTagZero = skippedTag0,
                missingTiles = missing
            };
            BuildRenderMesh(lvl, floors, ceilings, byTile, ref r);
            Connectivity(lvl, ref r);
            return r;
        }

        static Vector2 V(float u, float v) => new Vector2(u, v);

        static void EmitEdge(PidLevel lvl, PidTextureManifest manifest, int palette,
                             Dictionary<int, MeshAccum> byTile, MeshAccum col,
                             int x, int y, int slot,
                             ref int wallQuads, ref int colliders, ref int pillarColliders,
                             ref int skippedSolid, ref int skippedTag0, ref int missing)
        {
            int nx = slot == 0 ? x : x - 1;
            int ny = slot == 0 ? y - 1 : y;

            bool aVoid = lvl.IsVoidAt(x, y), bVoid = lvl.IsVoidAt(nx, ny);
            bool aBlk = lvl.IsBlocking(x, y), bBlk = lvl.IsBlocking(nx, ny);
            int word = lvl.Word(x, y, slot);
            int tag = PidConst.Tag(word);

            //Collision, decided independently of geometry since we don't actually have floor and ceiling collision
            bool oneSideBlocks = aBlk != bBlk;
            if (oneSideBlocks || (!aBlk && !bBlk && lvl.Blocks(x, y, slot)))
            {
                EmitCollider(col, x, y, slot);
                colliders++;
                if (oneSideBlocks && tag == 0) pillarColliders++;
            }

            //Both sides void means the face is never seen and is culled: void sectors do carry real wall data, but only the ones areas the
            //player sees are visable
            if (aVoid && bVoid)
            {
                if (tag != 0) skippedSolid++;
                return;
            }

            //Tag 0 is never drawn, because its void
            if (tag == 0) { skippedTag0++; return; }

            int res = PidConst.Resource(word);
            int s1 = PidConst.S1Index(word);

            if (manifest?.Get(res)?.Get(s1) == null)
            {
                missing++;
                if (!drawMissingTiles) return;
            }
            int key = manifest?.Get(res)?.Get(s1) == null
                    ? TileKeyMissing : TileKey(res, s1, palette);

            //Tile heights are pixel rows, not world units, since PID just stretches the art to fill the wall
            EmitQuad(byTile, key, x, y, slot, tag);
            wallQuads++;
        }

        public const int TileKeyMissing = -1;

        //Draw something visible where a tile is missing
        public static bool drawMissingTiles = false;

        //Holdovers from when Unity was generating and drawing floors and ceilings, goes unused now but its too much work to take out
        public static bool drawFloors = false;
        public static bool drawCeilings = false;

        //LUT palette has to be part of the key, or two levels using the same tile in different colors would share a material
        public static int TileKey(int resource, int s1Index, int palette) =>
            (resource * 1000 + s1Index) * 10 + palette;

        public static void SplitTileKey(int key, out int resource, out int s1Index, out int palette)
        {
            palette = key % 10;
            int rest = key / 10;
            resource = rest / 1000;
            s1Index = rest % 1000;
        }

        //How much of the cell edge this wall actually covers. Always full height, just not always full width
        static void TagSpan(int tag, out float t0, out float t1)
        {
            switch (tag)
            {
                case 2: t0 = 256f / 1024f; t1 = 1f; break;
                case 3: t0 = 0f; t1 = 768f / 1024f; break;
                case 4: t0 = 256f / 1024f; t1 = 768f / 1024f; break;
                default: t0 = 0f; t1 = 1f; break;
            }
        }

        //The angled bits where two walls meet, each one plugs the notch that a partial wall leaves at the corner
        static readonly float[,] CornerPts =
        {
            { 768f,    0f, 1024f,  256f },   //slot 2 NE
            { 256f,    0f,    0f,  256f },   //slot 3 NW
            { 768f, 1024f, 1024f,  768f },   //slot 4 SE
            {   0f,  768f,  256f, 1024f },   //slot 5 SW
        };

        static void EmitCorner(PidLevel lvl, PidTextureManifest manifest, int palette,
                               Dictionary<int, MeshAccum> byTile,
                               int x, int y, int slot,
                               ref int wallQuads, ref int missing)
        {
            int word = lvl.Word(x, y, slot);
            if (PidConst.Tag(word) == 0) return;

            int res = PidConst.Resource(word);
            int s1 = PidConst.S1Index(word);
            bool have = manifest?.Get(res)?.Get(s1) != null;
            if (!have) { missing++; if (!drawMissingTiles) return; }

            int key = have ? TileKey(res, s1, palette) : TileKeyMissing;
            if (!byTile.TryGetValue(key, out var m)) byTile[key] = m = new MeshAccum();

            int r = slot - 2;
            float ax = Mathf.Lerp(XW(x), XW(x + 1), CornerPts[r, 0] / 1024f);
            float az = Mathf.Lerp(ZN(y), ZN(y + 1), CornerPts[r, 1] / 1024f);
            float bx = Mathf.Lerp(XW(x), XW(x + 1), CornerPts[r, 2] / 1024f);
            float bz = Mathf.Lerp(ZN(y), ZN(y + 1), CornerPts[r, 3] / 1024f);
            float h = PidConst.WallHeight;

            m.QuadBoth(new Vector3(ax, 0f, az), new Vector3(bx, 0f, bz),
                       new Vector3(bx, h, bz), new Vector3(ax, h, az),
                       V(0, 0), V(1, 0), V(1, 1), V(0, 1));
            wallQuads++;
        }

        static void EmitQuad(Dictionary<int, MeshAccum> byTile, int key,
                             int x, int y, int slot, int tag)
        {
            if (!byTile.TryGetValue(key, out var m)) byTile[key] = m = new MeshAccum();
            float h = PidConst.WallHeight;

            //The texture is pinned to the world, so a tag-4 wall shows the middle half of the tile
            TagSpan(tag, out float t0, out float t1);

            if (slot == 0)
            {
                float a = Mathf.Lerp(XW(x), XW(x + 1), t0);
                float b = Mathf.Lerp(XW(x), XW(x + 1), t1);
                float z = ZN(y);
                m.QuadBoth(new Vector3(a, 0f, z), new Vector3(b, 0f, z),
                           new Vector3(b, h, z), new Vector3(a, h, z),
                           V(t0, 0), V(t1, 0), V(t1, 1), V(t0, 1));
            }
            else
            {
                float xw = XW(x);
                float a = Mathf.Lerp(ZN(y), ZN(y + 1), t0);
                float b = Mathf.Lerp(ZN(y), ZN(y + 1), t1);
                m.QuadBoth(new Vector3(xw, 0f, a), new Vector3(xw, 0f, b),
                           new Vector3(xw, h, b), new Vector3(xw, h, a),
                           V(t0, 0), V(t1, 0), V(t1, 1), V(t0, 1));
            }
        }

        static void EmitCollider(MeshAccum col, int x, int y, int slot)
        {
            //Colliders are always full height regardless of the drawn tile
            float h = PidConst.WallHeight;
            if (slot == 0)
            {
                float x0 = XW(x), x1 = XW(x + 1), z = ZN(y);
                col.QuadBoth(new Vector3(x0, 0f, z), new Vector3(x1, 0f, z),
                             new Vector3(x1, h, z), new Vector3(x0, h, z),
                             V(0, 0), V(1, 0), V(1, 1), V(0, 1));
            }
            else
            {
                float xw = XW(x), zn = ZN(y), zs = ZN(y + 1);
                col.QuadBoth(new Vector3(xw, 0f, zn), new Vector3(xw, 0f, zs),
                             new Vector3(xw, h, zs), new Vector3(xw, h, zn),
                             V(0, 0), V(1, 0), V(1, 1), V(0, 1));
            }
        }

        static Mesh ToMesh(MeshAccum m, string name)
        {
            var mesh = new Mesh { name = name };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(m.verts);
            mesh.SetNormals(m.norms);
            mesh.SetUVs(0, m.uvs);
            mesh.SetTriangles(m.tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        //0 is floors, 1 is ceilings, then each wall type has its own number
        //Each tile is its own seperate texture, so each needs its own material
        static void BuildRenderMesh(PidLevel lvl, MeshAccum floors, MeshAccum ceilings,
                                    Dictionary<int, MeshAccum> byTile, ref Result r)
        {
            var verts = new List<Vector3>(floors.verts);
            var norms = new List<Vector3>(floors.norms);
            var uvs = new List<Vector2>(floors.uvs);
            var subs = new List<List<int>> { new List<int>(floors.tris) };
            var keys = new List<int>();

            {
                int b = verts.Count;
                verts.AddRange(ceilings.verts);
                norms.AddRange(ceilings.norms);
                uvs.AddRange(ceilings.uvs);
                var t = new List<int>(ceilings.tris.Count);
                foreach (int i in ceilings.tris) t.Add(i + b);
                subs.Add(t);
            }

            foreach (var kv in byTile)
            {
                int b = verts.Count;
                verts.AddRange(kv.Value.verts);
                norms.AddRange(kv.Value.norms);
                uvs.AddRange(kv.Value.uvs);
                var t = new List<int>(kv.Value.tris.Count);
                foreach (int i in kv.Value.tris) t.Add(i + b);
                subs.Add(t);
                keys.Add(kv.Key);
            }

            var mesh = new Mesh { name = $"PID_L{lvl.level_number:D2}_render" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = subs.Count;
            for (int i = 0; i < subs.Count; i++) mesh.SetTriangles(subs[i], i);
            mesh.RecalculateBounds();

            r.render = mesh;
            r.submeshTileKey = keys;
        }

        static Dictionary<int, Mesh> BuildMarkers(Dictionary<int, MeshAccum> marks, int lvlNum)
        {
            var outp = new Dictionary<int, Mesh>();
            foreach (var kv in marks)
                outp[kv.Key] = ToMesh(kv.Value, $"PID_L{lvlNum:D2}_mark{kv.Key}");
            return outp;
        }

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
                if (lvl.IsBlocking(sx, sy) || comp[start] != -1) continue;

                int size = 0;
                stack.Push(start); comp[start] = id;
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
                if (withArrival.Contains(c) || c == largestId) continue;
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
            if (!lvl.InBounds(bx, by) || lvl.IsBlocking(bx, by)) return;
            int bi = by * PidConst.Grid + bx;
            if (comp[bi] != -1) return;

            int ox = bx > ax ? bx : ax;
            int oy = by > ay ? by : ay;
            int slot = (ay != by) ? 0 : 1;
            if (lvl.Blocks(ox, oy, slot)) return;

            comp[bi] = id;
            stack.Push(bi);
        }
    }
}