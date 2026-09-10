using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace Pid
{
    //Eight big-endian words, indexed by item id
    public class PidCatalogEntry
    {
        public int id;
        public readonly int[] w = new int[8];

        //Copies of the two string lists, carried alongside the record so a panel can render without needing PidStrings
        public string name = "";
        public string examine = "";

        public int SpriteIndex => w[0] & 0x7F;

        //Class selects the use behaviour and the inventory suffix
        public int Class => w[1];

        public int Score => w[2];

        //Weight in twenty-eighths of a kilogram, how PID did it (not my insanity)
        public int Weight => w[3];

        //What this item contributes to the fill of whatever contains it
        public int Fill => w[4];

        public int Treasure => w[5];

        //Nonzero means container
        public int Limit => w[6];

        //Which ids this container accepts
        public int Compat => w[7];

        public bool IsContainer => w[6] > 0;

        //The four broken guns carry live Limit and Compat but class zero, so they load magazines, but trying to equip them by force or
        //ID swapping crashes the game
        public bool IsWeapon => w[1] == PidCatalog.ClassWeapon;

        public bool Admits(int candidateId)
        {
            switch (Compat)
            {
                case PidCatalog.CompatAny: return true;
                case PidCatalog.CompatGrenades: return candidateId >= 58 && candidateId <= 60;
                case PidCatalog.CompatAkMags: return candidateId >= 53 && candidateId <= 55;
                default: return candidateId == Compat;
            }
        }
    }

    //The 71-entry item catalog.
    //
    //In PID this is not a resource since it lives in the compressed global initialiser and is expanded into the A5 world once at startup
    //It arrives here as an export since it makes more since for Unity, and the extractor will pull it as a JSON in the final build
    public class PidCatalog
    {
        public const int Count = 71;

        //Class values that actually occur, since nothing maps to 1 or 9 for some reason
        public const int ClassNone = 0;
        public const int ClassPotion = 2;
        public const int ClassWeapon = 3;
        public const int ClassCrystal = 4;
        public const int ClassSpecial = 5;

        public const int CompatAny = 0xFFFF;
        public const int CompatGrenades = 0xFFFB;   //40mm cartridges, ids 58-60, for ammo types
        public const int CompatAkMags = 0xFFFA;     //AK magazines, ids 53-55, for ammo types

        //Twenty-eighths of a kilogram, PID divides by this in the drawing code
        public const int WeightUnitsPerKg = 28;

        //Ids 51 to 57 are magazines, their inventory word 2 is a round count
        public const int FirstMagazineId = 51;
        public const int LastMagazineId = 57;

        //The Red Velvet Bag, PID doesn't count the weight of anything inside it...for some reason, because weight doesn't actually matter in PID
        public const int WeightlessContainerId = 9;

        public const int CedarBoxId = 8;

        //Cedar Box admits only these fourteen ids, on top of the ordinary Compat and Limit gates
        //The clone that the box produces bypasses this list entirely
        public static readonly int[] CedarAdmitIds =
            { 2, 45, 46, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61 };

        public const string RoundCountFormat = " (x{0})";

        readonly PidCatalogEntry[] entries = new PidCatalogEntry[Count];

        public PidCatalogEntry Get(int id)
            => id >= 0 && id < Count ? entries[id] : null;

        public string Name(int id)
        {
            var e = Get(id);
            return e != null ? e.name : "";
        }

        public string Examine(int id)
        {
            var e = Get(id);
            return e != null ? e.examine : "";
        }

        public static bool IsMagazine(int id)
            => id >= FirstMagazineId && id <= LastMagazineId;

        //**********Loading**********
        public static PidCatalog Load(TextAsset json)
            => json == null ? null : Load(json.text);

        public static PidCatalog Load(string json)
        {
            List<CatalogDto> raw;
            try { raw = JsonConvert.DeserializeObject<List<CatalogDto>>(json); }
            catch (JsonException e)
            {
                Debug.LogError($"[PID] Item catalog parse failed: {e.Message}");
                return null;
            }
            if (raw == null) { Debug.LogError("[PID] Item catalog empty."); return null; }

            var cat = new PidCatalog();
            int loaded = 0;
            foreach (var d in raw)
            {
                if (d.id < 0 || d.id >= Count)
                {
                    Debug.LogWarning($"[PID] Catalog id {d.id} out of range, skipped.");
                    continue;
                }
                var e = new PidCatalogEntry { id = d.id, name = d.name ?? "", examine = d.examine ?? "" };
                d.CopyWords(e.w);
                cat.entries[d.id] = e;
                loaded++;
            }

            if (loaded != Count)
                Debug.LogError($"[PID] Item catalog has {loaded} of {Count} entries.");

            int scoreSum = 0, spriteHigh = 0;
            for (int i = 0; i < Count; i++)
            {
                var e = cat.entries[i];
                if (e == null) continue;
                scoreSum += e.Score;
                if ((e.w[0] & ~0x7F) != 0) spriteHigh++;
            }
            if (scoreSum != 44)
                Debug.LogError($"[PID] Catalog score sum is {scoreSum}, expected 44.");
            if (spriteHigh != 0)
                Debug.LogError($"[PID] {spriteHigh} entries have sprite bits above 7 set.");

            return cat;
        }

        class CatalogDto
        {
            public int id = -1;
            public string name;
            public string examine;
            public int[] w;
            public int w0, w1, w2, w3, w4, w5, w6, w7;

            public void CopyWords(int[] dest)
            {
                if (w != null && w.Length >= 8)
                {
                    for (int i = 0; i < 8; i++) dest[i] = w[i];
                    return;
                }
                dest[0] = w0; dest[1] = w1; dest[2] = w2; dest[3] = w3;
                dest[4] = w4; dest[5] = w5; dest[6] = w6; dest[7] = w7;
            }
        }

        //**********Inventory Label**********
        public static int SuffixIndex(int cls)
        {
            switch (cls)
            {
                case 3: return 2;
                case 4: return 3;
                case 5: return 5;
                case 6: return 1;
                case 7: return 4;
                case 8: return 5;
                default: return cls;
            }
        }

        public string Label(PidInvRecord rec, PidStrings str, bool suffixes = true)
        {
            if (rec == null || rec.id == 0xFFFF) return "";

            string line = str != null ? str.Get(2000, rec.id) : Name(rec.id);
            if (!suffixes) return line;

            if (IsMagazine(rec.id))
            {
                line += rec.word2 != 0
                    ? string.Format(RoundCountFormat, rec.word2)
                    : Suffix(str, 0);
            }

            if (rec.Equipped)
            {
                var e = Get(rec.id);
                line += Suffix(str, SuffixIndex(e != null ? e.Class : 0));
            }

            return line;
        }

        static string Suffix(PidStrings str, int index)
            => str != null ? str.Get(2008, index) : "";

        //**********Encumberance***********

        //Sum of Weight over the whole tree, in twenty-eighths of a kilogram, even though we don't really need this
        public int WeightRaw(PidPlayer p)
            => p == null ? 0 : SumChain(p, p.inventoryHead, 0);

        public float WeightKg(PidPlayer p)
            => WeightRaw(p) / (float)WeightUnitsPerKg;

        const int MaxDepth = 16;

        int SumChain(PidPlayer p, int slot, int depth)
        {
            int total = 0;
            int guard = 0;

            while (slot != 0xFFFF)
            {
                if (slot < 0 || slot >= p.inventory.Length) break;
                if (++guard > p.inventory.Length) break;

                var rec = p.inventory[slot];
                if (rec == null) break;

                //Red Velvet Bag moment
                if (rec.id != WeightlessContainerId)
                {
                    var e = Get(rec.id);
                    if (e != null)
                    {
                        total += e.Weight;
                        if (e.IsContainer && depth < MaxDepth)
                            total += SumChain(p, rec.word2, depth + 1);
                    }
                }

                slot = rec.word3;
            }

            return total;
        }

        //Summed Fill of a container's immediate children
        public int FillOf(PidPlayer p, int containerSlot)
        {
            if (p == null) return 0;
            var container = SlotAt(p, containerSlot);
            if (container == null) return 0;

            int total = 0, guard = 0;
            int slot = container.word2;
            while (slot != 0xFFFF)
            {
                if (slot < 0 || slot >= p.inventory.Length) break;
                if (++guard > p.inventory.Length) break;
                var rec = p.inventory[slot];
                if (rec == null) break;
                var e = Get(rec.id);
                if (e != null) total += e.Fill;
                slot = rec.word3;
            }
            return total;
        }

        //***********Insert Gate**********
        //The container accepts the id, and the limit covers whats already inside plus whats going in
        //Also covers guns, since they act as containers for a single mag
        public bool CanInsert(PidPlayer p, int containerSlot, int candidateId)
        {
            var container = SlotAt(p, containerSlot);
            if (container == null) return false;

            var ce = Get(container.id);
            var ie = Get(candidateId);
            if (ce == null || ie == null || !ce.IsContainer) return false;

            if (!ce.Admits(candidateId)) return false;
            if (ce.Limit < FillOf(p, containerSlot) + ie.Fill) return false;

            //The Cedar Box has one extra gate
            if (container.id == CedarBoxId && container.word2 == 0xFFFF)
            {
                bool listed = false;
                foreach (int allowed in CedarAdmitIds)
                    if (allowed == candidateId) { listed = true; break; }
                if (!listed) return false;
            }

            return true;
        }

        static PidInvRecord SlotAt(PidPlayer p, int slot)
        {
            if (p == null || slot < 0 || slot >= p.inventory.Length) return null;
            var rec = p.inventory[slot];
            return rec != null && rec.id != 0xFFFF ? rec : null;
        }
    }
}