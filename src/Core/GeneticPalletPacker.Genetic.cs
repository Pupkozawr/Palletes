using System;
using System.Collections.Generic;
using System.Linq;
using Palletes.Generation;
using Palletes.Models;

namespace Palletes.Core
{
    public static partial class GeneticPalletPacker
    {
        private static List<PlacedBox> PackSinglePallet(IReadOnlyList<PackBox> boxes, PalletSpec pallet, int seed, FitnessWeights weights)
        {
            int n = boxes.Count;
            if (n == 0) return new List<PlacedBox>();

            var rng = new Rng(seed);

            int populationSize = Math.Clamp(80 + n / 3, 80, 180);
            int generations = Math.Clamp(280 + n, 280, 700);
            int tournamentK = 3;

            var population = new Chromosome[populationSize];
            population[0] = CreateHeuristicChromosome(boxes, descendingVolume: true);
            Evaluate(population[0], boxes, pallet, weights);

            if (populationSize > 1)
            {
                population[1] = CreateHeuristicChromosome(boxes, descendingVolume: false);
                Evaluate(population[1], boxes, pallet, weights);
            }

            for (int i = 2; i < populationSize; i++)
            {
                population[i] = RandomChromosome(n, rng);
                Evaluate(population[i], boxes, pallet, weights);
            }

            Chromosome best = population.OrderByDescending(c => c.Fitness)
                .ThenByDescending(c => c.PlacedCount)
                .ThenBy(c => c.Height)
                .ThenBy(c => c.EmptyVolume)
                .First()
                .Clone();

            int noImprove = 0;
            int noImproveLimit = Math.Clamp(90 + n / 2, 90, 220);

            for (int gen = 0; gen < generations; gen++)
            {
                var next = new Chromosome[populationSize];
                next[0] = best.Clone();

                for (int i = 1; i < populationSize; i++)
                {
                    var p1 = Tournament(population, rng, tournamentK);
                    var p2 = Tournament(population, rng, tournamentK);

                    var child = Crossover(p1, p2, rng);
                    Mutate(child, rng);
                    Evaluate(child, boxes, pallet, weights);
                    next[i] = child;
                }

                population = next;

                var genBest = population.OrderByDescending(c => c.Fitness)
                    .ThenByDescending(c => c.PlacedCount)
                    .ThenBy(c => c.Height)
                    .ThenBy(c => c.EmptyVolume)
                    .First();

                if (IsBetter(genBest, best))
                {
                    best = genBest.Clone();
                    noImprove = 0;
                }
                else
                {
                    noImprove++;
                    if (noImprove >= noImproveLimit)
                        break;
                }
            }

            return Decode(best, boxes, pallet);
        }

        private static bool IsBetter(Chromosome candidate, Chromosome incumbent)
        {
            if (candidate.Fitness != incumbent.Fitness)
                return candidate.Fitness > incumbent.Fitness;

            if (candidate.PlacedCount != incumbent.PlacedCount)
                return candidate.PlacedCount > incumbent.PlacedCount;

            if (candidate.Height != incumbent.Height)
                return candidate.Height < incumbent.Height;

            return candidate.EmptyVolume < incumbent.EmptyVolume;
        }

        private static Chromosome CreateHeuristicChromosome(IReadOnlyList<PackBox> boxes, bool descendingVolume)
        {
            var order = Enumerable.Range(0, boxes.Count)
                .OrderBy(i => descendingVolume ? 0 : 1)
                .ThenByDescending(i => descendingVolume ? boxes[i].Volume : (long)boxes[i].L * boxes[i].W)
                .ThenByDescending(i => boxes[i].WeightGrams)
                .ThenByDescending(i => boxes[i].Strength)
                .ThenByDescending(i => boxes[i].H)
                .ThenBy(i => boxes[i].Id, StringComparer.Ordinal)
                .ToArray();

            if (!descendingVolume)
            {
                Array.Reverse(order);
            }

            var c = new Chromosome(boxes.Count);
            for (int i = 0; i < boxes.Count; i++)
            {
                c.Order[i] = order[i];
                c.Orientation[i] = ChooseHeuristicOrientation(boxes[order[i]]);
            }

            return c;
        }

        private static byte ChooseHeuristicOrientation(PackBox b)
        {
            byte bestOri = 0;
            long bestScore = long.MinValue;

            for (byte ori = 0; ori < 6; ori++)
            {
                var (l, w, h) = OrientedDims(b, ori);
                long score = (long)l * w * 10 - h;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestOri = ori;
                }
            }

            return bestOri;
        }

        private static Chromosome RandomChromosome(int n, Rng rng)
        {
            var c = new Chromosome(n);
            for (int i = 0; i < n; i++) c.Order[i] = i;

            for (int i = n - 1; i > 0; i--)
            {
                int j = rng.Int(0, i);
                (c.Order[i], c.Order[j]) = (c.Order[j], c.Order[i]);
            }

            for (int i = 0; i < n; i++) c.Orientation[i] = (byte)rng.Int(0, 5);
            return c;
        }

        private static Chromosome Tournament(Chromosome[] pop, Rng rng, int k)
        {
            var best = pop[rng.Int(0, pop.Length - 1)];
            for (int i = 1; i < k; i++)
            {
                var c = pop[rng.Int(0, pop.Length - 1)];
                if (IsBetter(c, best)) best = c;
            }
            return best;
        }

        private static Chromosome Crossover(Chromosome a, Chromosome b, Rng rng)
        {
            int n = a.Order.Length;
            var child = new Chromosome(n);

            int cut1 = rng.Int(0, n - 1);
            int cut2 = rng.Int(0, n - 1);
            if (cut1 > cut2) (cut1, cut2) = (cut2, cut1);

            var used = new bool[n];
            for (int i = cut1; i <= cut2; i++)
            {
                child.Order[i] = a.Order[i];
                child.Orientation[i] = a.Orientation[i];
                used[child.Order[i]] = true;
            }

            int write = (cut2 + 1) % n;
            int read = (cut2 + 1) % n;
            for (int filled = cut2 - cut1 + 1; filled < n;)
            {
                int gene = b.Order[read];
                if (!used[gene])
                {
                    child.Order[write] = gene;
                    child.Orientation[write] = b.Orientation[read];
                    used[gene] = true;
                    write = (write + 1) % n;
                    filled++;
                }
                read = (read + 1) % n;
            }

            return child;
        }

        private static void Mutate(Chromosome c, Rng rng)
        {
            int n = c.Order.Length;

            if (n >= 2 && rng.Bool(0.35))
            {
                int i = rng.Int(0, n - 1);
                int j = rng.Int(0, n - 1);
                (c.Order[i], c.Order[j]) = (c.Order[j], c.Order[i]);
                (c.Orientation[i], c.Orientation[j]) = (c.Orientation[j], c.Orientation[i]);
            }

            if (n >= 3 && rng.Bool(0.20))
            {
                int i = rng.Int(0, n - 1);
                int j = rng.Int(0, n - 1);
                if (i > j) (i, j) = (j, i);
                while (i < j)
                {
                    (c.Order[i], c.Order[j]) = (c.Order[j], c.Order[i]);
                    (c.Orientation[i], c.Orientation[j]) = (c.Orientation[j], c.Orientation[i]);
                    i++;
                    j--;
                }
            }

            if (rng.Bool(0.45))
            {
                int i = rng.Int(0, n - 1);
                c.Orientation[i] = (byte)rng.Int(0, 5);
            }
        }

    }
}
