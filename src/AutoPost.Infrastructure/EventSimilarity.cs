using System.Text.RegularExpressions;
namespace AutoPost.Infrastructure;

public static class EventSimilarity
{
    public static double Title(string left, string right)
    {
        static HashSet<string> Terms(string s) => Regex.Matches(s.ToLowerInvariant(), @"[\p{L}\p{N}]+").Select(m => m.Value).Where(v => v.Length > 1).ToHashSet();
        var a = Terms(left); var b = Terms(right);
        return a.Count < 4 || b.Count < 4 ? 0 : (double)a.Intersect(b).Count() / a.Union(b).Count();
    }
    public static double Cosine(float[] a, float[] b)
    {
        if (a.Length == 0 || a.Length != b.Length || a.Any(x => !float.IsFinite(x)) || b.Any(x => !float.IsFinite(x))) return 0;
        double dot = 0, aa = 0, bb = 0;
        for (var i = 0; i < a.Length; i++) { dot += (double)a[i] * b[i]; aa += (double)a[i] * a[i]; bb += (double)b[i] * b[i]; }
        return aa == 0 || bb == 0 ? 0 : dot / Math.Sqrt(aa * bb);
    }
}
