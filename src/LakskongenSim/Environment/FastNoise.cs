namespace LakskongenSim.Environment
{
    /// <summary>
    /// FastNoiseLite-inspired noise generator with ridged fractal and domain warp.
    /// Simplified implementation inspired by FastNoiseLite (Auburn/FastNoiseLite).
    /// </summary>
    public sealed class FastNoise
    {
        private readonly uint _seed;
        private readonly int _fractalOctaves;
        private readonly float _fractalLacunarity;
        private readonly float _fractalGain;
        private readonly float _frequency;
        
        // Domain warp parameters
        private readonly float _warpAmplitude;
        private readonly float _warpFrequency;
        
        // Ridged fractal parameters
        private readonly float _ridgedOffset;
        private readonly float _ridgedGain;

        public FastNoise(uint seed, int fractalOctaves = 4, float frequency = 0.01f,
            float lacunarity = 2.0f, float gain = 0.5f,
            float warpAmplitude = 30.0f, float warpFrequency = 0.5f,
            float ridgedOffset = 1.0f, float ridgedGain = 0.5f)
        {
            _seed = seed;
            _fractalOctaves = fractalOctaves;
            _frequency = frequency;
            _fractalLacunarity = lacunarity;
            _fractalGain = gain;
            _warpAmplitude = warpAmplitude;
            _warpFrequency = warpFrequency;
            _ridgedOffset = ridgedOffset;
            _ridgedGain = ridgedGain;
        }

        /// <summary>
        /// Get ridged fractal noise with domain warp at the given coordinates.
        /// Returns value in [-1, 1] range.
        /// </summary>
        public float GetNoise(float x, float z)
        {
            // Domain warp: perturb input coordinates using another noise function
            float warpX = x + _warpAmplitude * GetWarpNoise(x * _warpFrequency, z * _warpFrequency + 1000f);
            float warpZ = z + _warpAmplitude * GetWarpNoise(x * _warpFrequency + 1000f, z * _warpFrequency);

            return GetRidgedNoise(warpX * _frequency, warpZ * _frequency);
        }

        /// <summary>
        /// Get noise value in [0, 1] range for domain warping.
        /// </summary>
        private float GetWarpNoise(float x, float z)
        {
            uint hash = Hash((int)(x * 1000), (int)(z * 1000));
            return (hash & 0xFFFFFF) / 16777215.0f * 2.0f - 1.0f;
        }

        /// <summary>
        /// Get ridged multifractal noise.
        /// </summary>
        private float GetRidgedNoise(float x, float z)
        {
            float amplitude = 1.0f;
            float frequency = 1.0f;
            float signal = 0f;
            float weight = 1.0f;

            for (int i = 0; i < _fractalOctaves; i++)
            {
                float nx = x * frequency;
                float nz = z * frequency;
                
                // Perlin noise (simplified)
                float n = PerlinNoise(nx, nz);
                
                // Ridged: abs(n) with offset
                n = _ridgedOffset - Math.Abs(n);
                n = n * n * _ridgedGain;
                
                // Weighted sum for multifractal
                signal += n * amplitude * weight;
                weight = Math.Clamp(signal * _fractalGain, 0f, 1f);
                amplitude *= _fractalGain;
                frequency *= _fractalLacunarity;
            }

            return Math.Clamp(signal, -1f, 1f);
        }

        /// <summary>
        /// Simplified 2D Perlin noise.
        /// </summary>
        private float PerlinNoise(float x, float z)
        {
            int ix = (int)Math.Floor(x);
            int iz = (int)Math.Floor(z);
            float fx = x - ix;
            float fz = z - iz;

            // Fade curves
            float u = Fade(fx);
            float v = Fade(fz);

            // Hash corners
            float n00 = GradientNoise(ix, iz, fx, fz);
            float n10 = GradientNoise(ix + 1, iz, fx - 1, fz);
            float n01 = GradientNoise(ix, iz + 1, fx, fz - 1);
            float n11 = GradientNoise(ix + 1, iz + 1, fx - 1, fz - 1);

            // Bilinear interpolation
            float nx0 = Lerp(n00, n10, u);
            float nx1 = Lerp(n01, n11, u);
            return Lerp(nx0, nx1, v);
        }

        private float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);

        private float Lerp(float a, float b, float t) => a + (b - a) * t;

        private float GradientNoise(int ix, int iz, float fx, float fz)
        {
            uint hash = Hash(ix, iz);
            // 4 gradient directions
            float angle = (hash & 3) * MathF.PI / 2;
            float gx = MathF.Cos(angle);
            float gz = MathF.Sin(angle);
            return gx * fx + gz * fz;
        }

        private uint Hash(int x, int z)
        {
            uint h = _seed;
            h ^= (uint)x * 0x9E3779B9;
            h = (h ^ (h >> 16)) * 0x85EBCA6B;
            h ^= (uint)z * 0x9E3779B9;
            h = (h ^ (h >> 16)) * 0x85EBCA6B;
            return h ^ (h >> 16);
        }
    }
}