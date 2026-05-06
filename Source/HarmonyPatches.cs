using HarmonyLib;
using UnityEngine;

namespace TargetInterceptPlanner
{
    [HarmonyPatch(typeof(OrbitRendererBase))]
    public static class OrbitRendererBasePatches
    {
        // forces hyperbolic orbits to not fade away over time

        [HarmonyPatch(nameof(OrbitRendererBase.SplineOpacityUpdate))]
        [HarmonyPrefix]
        internal static bool Prefix_SplineOpacityUpdate(OrbitRendererBase __instance)
        {
            if (!OrbitRendererHack.renderers.Contains(__instance)) return true;

            MapObject target = PlanetariumCamera.fetch?.target;
            CelestialBody tgt = target?.celestialBody ?? target?.orbit?.referenceBody;

            if (tgt == null)
            {
                Util.LogWarning("tgt is null!");
                return true;
            }

            if (__instance.driver == null)
            {
                Util.LogWarning("OrbitDriver is null!");
                return true;
            }

            Orbit orbit = __instance.driver.orbit;

            if (orbit == null)
            {
                Util.LogWarning("Orbit is null!");
                return true;
            }

            if (__instance.IsRenderableOrbit(orbit, tgt)) // TODO, use CamVsSmaRatio like stock and just make it really large? meh
            {
                __instance.lineOpacity = 1f;
            }
            else
            {
                Util.LogWarning("Orbit is not renderable!");
                return true;
            }

            return false;
        }

        // the code that determines how long to draw hyperbolic orbits is NOT in Orbit.DrawOrbit(), but instead in OrbitRendererBase.UpdateSpline()
        // Orbit.DrawOrbit() doesnt seem to ever get called? very strange

        internal const double drawAngle = 5d; // TODO, make this user-editable
        // original drawAngle is Math.Acos(0.0 - 1.0 / orbit.eccentricity);

        // massively increases how long hyperbolic orbits are drawn for

        [HarmonyPatch(nameof(OrbitRendererBase.UpdateSpline))]
        [HarmonyPrefix]
        internal static bool Prefix_UpdateSpline(OrbitRendererBase __instance)
        {
            if (!OrbitRendererHack.renderers.Contains(__instance)) return true;

            if (__instance.driver == null)
            {
                Util.LogWarning("OrbitDriver is null!");
                return true;
            }

            Orbit orbit = __instance.driver.orbit;

            if (orbit == null)
            {
                Util.LogWarning("Orbit is null!");
                return true;
            }

            if (orbit.eccentricity < 1d)
            {
                return true;
            }

            double st = -drawAngle;
            double end = drawAngle;
            double rng = end - st;
            double itv = rng / (double)(__instance.orbitPoints.Length - 1);
            int num3 = __instance.orbitPoints.Length;
            for (int i = 0; i < num3; i++)
            {
                __instance.orbitPoints[i] = orbit.getPositionFromEccAnomalyWithSemiMinorAxis(st + itv * (double)i, orbit.semiMinorAxis);
            }

            ScaledSpace.LocalToScaledSpace(__instance.orbitPoints, __instance.orbitLine.points3);

            __instance.orbitLine.drawEnd = __instance.orbitLine.points3.Count - 2;

            return false;
        }
    }


    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class HarmonyPatcher : MonoBehaviour
    {
        internal void Start()
        {
            var harmony = new Harmony("TargetInterceptPlanner.HarmonyPatcher");
            harmony.PatchAll();
        }
    }
}
