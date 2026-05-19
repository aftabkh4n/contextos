namespace ContextOS.Embeddings;

internal static class VectorMath
{
    /// <summary>Returns a new array that is the L2-normalized form of <paramref name="v"/>.</summary>
    internal static float[] L2Normalize(float[] v)
    {
        double norm = 0;
        for (int i = 0; i < v.Length; i++)
            norm += (double)v[i] * v[i];
        norm = Math.Sqrt(norm);

        if (norm < 1e-10) return v;

        float scale = (float)(1.0 / norm);
        float[] result = new float[v.Length];
        for (int i = 0; i < v.Length; i++)
            result[i] = v[i] * scale;
        return result;
    }
}
