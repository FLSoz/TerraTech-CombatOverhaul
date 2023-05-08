using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace CombatOverhaul
{
    internal static class Extensions
    {
        public static T2 GetOrNull<T1, T2>(this Dictionary<T1, T2> dict, T1 key)
        {
            if (key != null && dict.TryGetValue(key, out T2 value))
            {
                return value;
            }
            return default(T2);
        }
        public static T2 GetOrDefault<T1, T2>(this Dictionary<T1, T2> dict, T1 key, T2 defaultValue)
        {
            if (key != null && dict.TryGetValue(key, out T2 value))
            {
                return value;
            }
            return defaultValue;
        }

        // Taken from here: https://gamedev.stackexchange.com/questions/165643/how-to-calculate-the-surface-area-of-a-mesh
        // standard CC BY-SA 3.0 license, no changes made
        public static float CalculateSurfaceArea(this Mesh mesh)
        {
            var triangles = mesh.triangles;
            var vertices = mesh.vertices;

            double sum = 0.0;

            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 corner = vertices[triangles[i]];
                Vector3 a = vertices[triangles[i + 1]] - corner;
                Vector3 b = vertices[triangles[i + 2]] - corner;

                sum += Vector3.Cross(a, b).magnitude;
            }

            return (float)(sum / 2.0);
        }
    }
}
