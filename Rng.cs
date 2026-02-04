using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Palletes
{
    public sealed class Rng
    {
        private readonly Random _r;
        public Rng(int seed) => _r = new Random(seed);

        public int Int(int minInclusive, int maxInclusive)
            => _r.Next(minInclusive, maxInclusive + 1);

        public double Double() => _r.NextDouble();
        public double Double(double min, double max) => min + (max - min) * _r.NextDouble();
        public bool Bool(double pTrue) => _r.NextDouble() < pTrue;

        public void Shuffle<T>(T[] a)
        {
            for (int i = a.Length - 1; i > 0; i--)
            {
                int j = _r.Next(0, i + 1);
                (a[i], a[j]) = (a[j], a[i]);
            }
        }

        public void Shuffle(double[] a)
        {
            for (int i = a.Length - 1; i > 0; i--)
            {
                int j = _r.Next(0, i + 1);
                (a[i], a[j]) = (a[j], a[i]);
            }
        }

        public int WeightedIndex(double[] weights)
        {
            double sum = 0;
            for (int i = 0; i < weights.Length; i++) sum += Math.Max(0, weights[i]);
            if (sum <= 0) return _r.Next(0, weights.Length);

            double x = _r.NextDouble() * sum;
            double acc = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                acc += Math.Max(0, weights[i]);
                if (acc >= x) return i;
            }
            return weights.Length - 1;
        }

        public T WeightedPick<T>(IReadOnlyList<T> items, Func<T, double> weight)
        {
            double sum = 0;
            for (int i = 0; i < items.Count; i++) sum += Math.Max(0, weight(items[i]));
            if (sum <= 0) return items[_r.Next(0, items.Count)];

            double x = _r.NextDouble() * sum;
            double acc = 0;
            for (int i = 0; i < items.Count; i++)
            {
                acc += Math.Max(0, weight(items[i]));
                if (acc >= x) return items[i];
            }
            return items[^1];
        }

        public double Normal(double mean, double std)
        {
            double u1 = Math.Max(1e-12, _r.NextDouble());
            double u2 = Math.Max(1e-12, _r.NextDouble());
            double z0 = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            return mean + std * z0;
        }

        public double Gamma(double shape, double scale)
        {
            if (shape <= 0) return 0;

            if (shape < 1.0)
            {
                double u = Math.Max(1e-12, _r.NextDouble());
                return Gamma(shape + 1.0, scale) * Math.Pow(u, 1.0 / shape);
            }

            double d = shape - 1.0 / 3.0;
            double c = 1.0 / Math.Sqrt(9.0 * d);

            while (true)
            {
                double x = Normal(0, 1);
                double v = 1.0 + c * x;
                if (v <= 0) continue;
                v = v * v * v;

                double u = _r.NextDouble();
                if (u < 1.0 - 0.0331 * (x * x) * (x * x))
                    return scale * d * v;

                if (Math.Log(u) < 0.5 * x * x + d * (1.0 - v + Math.Log(v)))
                    return scale * d * v;
            }
        }
    }
}
