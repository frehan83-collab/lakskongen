namespace LakskongenSim.Environment
{
    /// <summary>
    /// 2D OpenSimplex2-style gradient noise (the "fast" variant): a skewed
    /// triangular lattice, a radial (0.5 − r²)⁴ kernel per lattice corner and 24
    /// evenly spaced unit gradients. Written for this project (no third-party
    /// code), replacing the old <c>FastNoise</c>, whose domain warp was an
    /// integer hash sampled every 0.001 units — white noise that threw every
    /// terrain vertex to a random location (review F464, F750).
    ///
    /// The result is continuous with a continuous gradient, isotropic to the
    /// eye and normalised to [−1, 1]. Only + − × and floor are used, so the
    /// value is bit-identical on every IEEE-754 platform: no sin/cos/pow, whose
    /// last bit differs between C runtimes.
    /// </summary>
    public static class OpenSimplex2
    {
        // (√3 − 1) / 2 and (3 − √3) / 6: the lattice skew and unskew factors.
        private const float Skew = 0.36602540378f;
        private const float Unskew = 0.21132486541f;
        private const float KernelRadiusSquared = 0.5f;
        // Peak of Σ (0.5 − r²)⁴ (g · d) for unit gradients is 1 / 99.83;
        // NoiseTests samples the field to keep the output inside [−1, 1].
        private const float Normalizer = 99.83685f;

        // Unit vectors at 7.5° + k·15°: no gradient lies on a lattice axis or
        // diagonal, so ridges never line up with the grid.
        private static readonly float[] Gradients =
        {
            0.991444861f, 0.130526192f,
            0.923879533f, 0.382683432f,
            0.793353340f, 0.608761429f,
            0.608761429f, 0.793353340f,
            0.382683432f, 0.923879533f,
            0.130526192f, 0.991444861f,
            -0.130526192f, 0.991444861f,
            -0.382683432f, 0.923879533f,
            -0.608761429f, 0.793353340f,
            -0.793353340f, 0.608761429f,
            -0.923879533f, 0.382683432f,
            -0.991444861f, 0.130526192f,
            -0.991444861f, -0.130526192f,
            -0.923879533f, -0.382683432f,
            -0.793353340f, -0.608761429f,
            -0.608761429f, -0.793353340f,
            -0.382683432f, -0.923879533f,
            -0.130526192f, -0.991444861f,
            0.130526192f, -0.991444861f,
            0.382683432f, -0.923879533f,
            0.608761429f, -0.793353340f,
            0.793353340f, -0.608761429f,
            0.923879533f, -0.382683432f,
            0.991444861f, -0.130526192f,
        };

        private const int GradientCount = 24;

        /// <summary>Noise at (x, y) in lattice units; the value lies in [−1, 1].</summary>
        public static float Noise2(int seed, float x, float y)
        {
            // Skew into lattice space and find the containing simplex.
            float s = (x + y) * Skew;
            int i = FastFloor(x + s);
            int j = FastFloor(y + s);
            float t = (i + j) * Unskew;
            float x0 = x - (i - t);
            float y0 = y - (j - t);

            // Lower or upper triangle of the skewed unit square.
            int i1 = x0 > y0 ? 1 : 0;
            int j1 = 1 - i1;
            float x1 = x0 - i1 + Unskew;
            float y1 = y0 - j1 + Unskew;
            float x2 = x0 - 1f + 2f * Unskew;
            float y2 = y0 - 1f + 2f * Unskew;

            float n = Corner(seed, i, j, x0, y0)
                + Corner(seed, i + i1, j + j1, x1, y1)
                + Corner(seed, i + 1, j + 1, x2, y2);
            float v = n * Normalizer;
            return v > 1f ? 1f : (v < -1f ? -1f : v);
        }

        private static float Corner(int seed, int i, int j, float dx, float dy)
        {
            float a = KernelRadiusSquared - dx * dx - dy * dy;
            if (a <= 0f)
                return 0f;
            int g = GradientIndex(seed, i, j) * 2;
            a *= a;
            return a * a * (Gradients[g] * dx + Gradients[g + 1] * dy);
        }

        private static int GradientIndex(int seed, int i, int j)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u;
                h ^= (uint)i * 0x85EBCA77u;
                h = (h ^ (h >> 15)) * 0xC2B2AE3Du;
                h ^= (uint)j * 0x27D4EB2Fu;
                h = (h ^ (h >> 13)) * 0x165667B1u;
                h ^= h >> 16;
                return (int)(h % GradientCount);
            }
        }

        private static int FastFloor(float v)
        {
            int i = (int)v;
            return v < i ? i - 1 : i;
        }
    }

    /// <summary>
    /// Fractal sums over <see cref="OpenSimplex2"/>. Each octave draws its own
    /// seed so octaves do not share lattice alignment.
    /// </summary>
    public static class FractalNoise
    {
        /// <summary>Fractional Brownian motion, normalised by the amplitude sum to [−1, 1].</summary>
        public static float Fbm(int seed, float x, float y, int octaves, float lacunarity = 2f, float gain = 0.5f)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * OpenSimplex2.Noise2(seed + o * 7919, x, y);
                norm += amp;
                amp *= gain;
                x *= lacunarity;
                y *= lacunarity;
            }
            return sum / norm;
        }

        /// <summary>
        /// Ridged multifractal in [0, 1]: crests where the noise crosses zero,
        /// each octave weighted by the previous one so detail gathers on the
        /// ridges and the valleys between them stay smooth. <paramref name="softness"/>
        /// rounds the crests (|n| becomes √(n² + k²) − k): a hard crease has an
        /// unbounded slope at heightfield resolution.
        /// </summary>
        public static float Ridged(int seed, float x, float y, int octaves, float lacunarity = 2f, float gain = 0.5f, float softness = 0f)
        {
            float sum = 0f, amp = 1f, norm = 0f, weight = 1f;
            for (int o = 0; o < octaves; o++)
            {
                float n = OpenSimplex2.Noise2(seed + o * 7919, x, y);
                float a = softness > 0f ? MathF.Sqrt(n * n + softness * softness) - softness : MathF.Abs(n);
                float r = 1f - a;
                r *= r;
                sum += amp * r * weight;
                norm += amp;
                weight = r;
                amp *= gain;
                x *= lacunarity;
                y *= lacunarity;
            }
            return sum / norm;
        }
    }
}
