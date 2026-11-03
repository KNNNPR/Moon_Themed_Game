using System;

namespace MoonThemedGame.Repair
{
    public static class RepairDamagePlan
    {
        // Select slots, not part types: three missing wheels are three distinct slots.
        public static int[] Select(int slotCount, int minimum, int maximum, int seed)
        {
            if (slotCount <= 0) return Array.Empty<int>();
            minimum = Math.Max(1, Math.Min(minimum, slotCount));
            maximum = Math.Max(minimum, Math.Min(maximum, slotCount));
            var random = new Random(seed);
            int count = random.Next(minimum, maximum + 1);
            var slots = new int[slotCount];
            for (int i = 0; i < slotCount; i++) slots[i] = i;
            for (int i = 0; i < count; i++)
            {
                int next = random.Next(i, slotCount);
                int old = slots[i]; slots[i] = slots[next]; slots[next] = old;
            }
            var result = new int[count];
            Array.Copy(slots, result, count);
            return result;
        }
    }
}
