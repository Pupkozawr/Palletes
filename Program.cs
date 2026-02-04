using Palletes;
using System.Diagnostics;
namespace Palletes
{
    internal class Program
    {
        public static int Main(string[] args)
        {
            var outDir = args.Length >= 1 ? args[0] : "out";
            var seed = args.Length >= 2 && int.TryParse(args[1], out var s) ? s : 12345;

            Directory.CreateDirectory(outDir);

            var rng = new Rng(seed);
            var profile = Profile.DefaultRetailLike();

            var pallet = new PalletSpec
            {
                PalletType = "EUR",
                Length = 1200,
                Width = 800,
                MaxHeight = 2000
            };

            var gen = new DatasetGenerator(profile, pallet, rng);
            var totalStopwatch = Stopwatch.StartNew();

            gen.GenerateAll(outDir);

            totalStopwatch.Stop();

            Console.WriteLine($"Done. Seed={seed}. Output: {Path.GetFullPath(outDir)}");
            Console.WriteLine($"Total generation time: {totalStopwatch.ElapsedMilliseconds} ms ({totalStopwatch.Elapsed:hh\\:mm\\:ss\\.fff})");

            return 0;
        }
    }
}
