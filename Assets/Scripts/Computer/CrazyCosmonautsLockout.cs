using System;
using System.Globalization;
using UnityEngine;

namespace MoonThemedGame.Computer
{
    // UTC persists through app closure, timeScale changes, and restarting the game.
    public sealed class CrazyCosmonautsLockout
    {
        readonly string key;
        readonly Func<double> clock;
        double until;
        public int Remaining => (int)Math.Min(int.MaxValue, Math.Ceiling(Math.Max(0, until - clock())));
        public CrazyCosmonautsLockout(string key, Func<double> clock = null)
        {
            this.key = key;
            this.clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d);
            double.TryParse(PlayerPrefs.GetString(key, "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out until);
            if (double.IsNaN(until) || double.IsInfinity(until)) until = 0;
        }
        public void Start(int seconds)
        {
            until = clock() + Math.Max(0, seconds);
            PlayerPrefs.SetString(key, until.ToString("R", CultureInfo.InvariantCulture));
            PlayerPrefs.Save();
        }
    }
}
