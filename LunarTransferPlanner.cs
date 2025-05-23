/*---------------------------------------------------------------------------
License: The code is subject to the MIT license (see below).
------------------------------------------------
Copyright (c) 2022 RCrockford

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
---------------------------------------------------------------------------*/

using System;
using System.ComponentModel;
using System.Text;
using UnityEngine;
using System.Linq;
using Contracts.Agents.Mentalities;
using KerbalConstructionTime;

namespace LunarTransferPlanner
{
    public static class Util
    {
        public static Vector3 x = Vector3.right;
        public static Vector3 y = Vector3.up;
        public static Vector3 z = Vector3.forward;
        public static void TryReadValue<T>(ref T target, ConfigNode node, string name)
        {
            if (node.HasValue(name))
            {
                try
                {
                    target = (T)TypeDescriptor.GetConverter(typeof(T)).ConvertFromString(node.GetValue(name));
                }
                catch
                {
                    // just skip over it
                }
            }
            // skip again
        }

        public static float Floor(float x, int digits)
        {
            int lol = 1;
            while (digits > 0)
            {
                lol *= 10;
                --digits;
            }
            return (float)((int)(x * lol)) / lol;
        }

        // Math.Acosh does not seem to work in .NET 4
        public static double Acosh(double x)
        {
            if (x >= 1)
            {
                return Math.Log(x + Math.Sqrt(x * x - 1));
            }
            else
            {
                return Double.NaN;
            }

        }
    }

    [KSPAddon(KSPAddon.Startup.FlightAndKSC, false)]
    public class LunarTransferPlanner : DaMichelToolbarSuperWrapper.PluginWithToolbarSupport
    {
        // main window
        Rect windowRect = new Rect(100, 100, -1, -1);
        bool isWindowOpen = true;
        // gui stuff
        float flightTime = 4f;
        float nextTickFT = 0f;
        float tliAltitudeKM = 200f;       // Parking orbit altitude (circular orbit assumed)
        float nextTickTA = 0f;
        bool showInfo = false;       // Expand/collapse tooltips
        bool showParking0 = false;       // Expand/collapse time in parking orbit for launch now
        bool showParking1 = false;       // Expand/collapse time in parking orbit for first window
        bool showParking2 = false;       // Expand/collapse time in parking orbit for second window
        bool showAltitude = false;       // Expand/collapse parking orbit altitude changer
        float warpMargin = 60f;
        float nextTickWM = 0f;
        string windowTitle = "";
        GUISkin skin;

        #region boring stuff
        protected override DaMichelToolbarSuperWrapper.ToolbarInfo GetToolbarInfo()
        {
            return new DaMichelToolbarSuperWrapper.ToolbarInfo
            {
                name = "LunarTransferPlanner",
                tooltip = "LunarTransferPlanner Show/Hide Gui",
                toolbarTexture = "LunarTransferPlanner/toolbarbutton",
                launcherTexture = "LunarTransferPlanner/launcherbutton",
                visibleInScenes = new GameScenes[] { GameScenes.FLIGHT, GameScenes.SPACECENTER }
            };
        }

        void Awake()
        {
            skin = (GUISkin)GUISkin.Instantiate(HighLogic.Skin);
            skin.button.padding = new RectOffset(2, 2, 2, 2);
            skin.button.margin = new RectOffset(1, 1, 1, 1);
            skin.box.padding = new RectOffset(2, 2, 2, 2);
            skin.box.margin = new RectOffset(1, 1, 1, 1);
            skin.textField.margin = new RectOffset(3, 1, 1, 1);
            skin.textField.padding = new RectOffset(4, 2, 1, 0);

            LoadSettings();
            InitializeToolbars();
            OnGuiVisibilityChange();
        }

        public void Start()
        {
            KACWrapper.InitKACWrapper();
        }

        public void OnDestroy()
        {
            SaveSettings();
            TearDownToolbars();
        }


        protected override void OnGuiVisibilityChange()
        {
            isWindowOpen = isGuiVisible;
        }

        void SaveSettings()
        {
            ConfigNode settings = new ConfigNode();
            settings.name = "LUNAR_TRANSFER_SETTINGS";
            SaveMutableToolbarSettings(settings);
            SaveImmutableToolbarSettings(settings);
            settings.AddValue("windowRect.xMin", windowRect.xMin);
            settings.AddValue("windowRect.yMin", windowRect.yMin);
            settings.AddValue("flightTime", flightTime);
            settings.AddValue("tliAltitudeKM", tliAltitudeKM);
            settings.AddValue("warpMargin", warpMargin);
            settings.Save(AssemblyLoader.loadedAssemblies.GetPathByType(typeof(LunarTransferPlanner)) + "/settings.cfg");
        }


        void LoadSettings()
        {
            ConfigNode settings = ConfigNode.Load(AssemblyLoader.loadedAssemblies.GetPathByType(typeof(LunarTransferPlanner)) + "/settings.cfg");
            if (settings != null)
            {
                float x = windowRect.xMin, y = windowRect.yMin;
                Util.TryReadValue(ref x, settings, "windowRect.xMin");
                Util.TryReadValue(ref y, settings, "windowRect.yMin");
                Util.TryReadValue(ref flightTime, settings, "flightTime");
                Util.TryReadValue(ref tliAltitudeKM, settings, "tliAltitudeKM");
                Util.TryReadValue(ref warpMargin, settings, "warpMargin");
                windowRect = new Rect(x, y, windowRect.width, windowRect.height);
                LoadMutableToolbarSettings(settings);
                LoadImmutableToolbarSettings(settings);
            }
        }
        #endregion

        void OnGUI()
        {
            if (isWindowOpen && buttonVisible)
            {
                GUI.skin = this.skin;
                windowRect = GUILayout.Window(this.GetHashCode(), windowRect, MakeMainWindow, windowTitle);
                float left = Mathf.Clamp(windowRect.x, 0, Screen.width - windowRect.width);
                float top = Mathf.Clamp(windowRect.y, 0, Screen.height - windowRect.height);
                windowRect = new Rect(left, top, windowRect.width, windowRect.height);
            }
        }

        private void MakeNumberEditField(ref float value, ref float nextTick, float step, float minValue)
        {
            GUILayout.BeginHorizontal();
            string textValue = value.ToString();
            string newlabel = GUILayout.TextField(textValue, GUILayout.MinWidth(40));
            if (textValue != newlabel)
            {
                float newvalue;
                if (float.TryParse(newlabel, out newvalue))
                {
                    value = newvalue;
                }
            }
            bool hitMinusButton = GUILayout.RepeatButton("-", GUILayout.MinWidth(16));
            bool hitPlusButton = GUILayout.RepeatButton("+", GUILayout.MinWidth(16));
            if (hitPlusButton || hitMinusButton)
            {
                float tick = Time.realtimeSinceStartup;
                if (tick > nextTick)
                {
                    if (hitMinusButton)
                        value = Mathf.Max(minValue, value - step);
                    else
                        value += step;
                    value = Mathf.Round(value / step) * step;

                    nextTick = tick + 0.2f;
                }
            }
            GUILayout.EndHorizontal();
        }

        public static PQSCity FindKSC(CelestialBody home)
        {
            if (home != null)
            {
                if (home.pqsController != null && home.pqsController.transform != null)
                {
                    Transform t = home.pqsController.transform.Find("KSC");
                    if (t != null)
                    {
                        PQSCity KSC = (PQSCity)t.GetComponent(typeof(PQSCity));
                        if (KSC != null) { return KSC; }
                    }
                }
            }

            PQSCity[] cities = Resources.FindObjectsOfTypeAll<PQSCity>();
            foreach (PQSCity c in cities)
            {
                if (c.name == "KSC")
                {
                    return c;
                }
            }

            return null;
        }

        private Vector3d GetLaunchPos(CelestialBody mainBody, ref double Latitude)
        {
            if (SpaceCenter.Instance != null)
            {
                PQSCity ksc = FindKSC(FlightGlobals.GetHomeBody());
                if (ksc)
                {
                    Latitude = ksc.lat;
                    return mainBody.GetWorldSurfacePosition(ksc.lat, ksc.lon, 0);
                }
                else
                {
                    Latitude = SpaceCenter.Instance.Latitude;
                    return mainBody.GetWorldSurfacePosition(SpaceCenter.Instance.Latitude, SpaceCenter.Instance.Longitude, 0);
                }
            }

            return Vector3d.zero;
        }

        private readonly struct OrbitData
        {
            public OrbitData(Vector3d n, double i, double a)
            {
                normal = n;
                inclination = i;
                azimuth = a;
            }

            public readonly Vector3d normal;
            public readonly double inclination;
            public readonly double azimuth;
        }

        private OrbitData CalcOrbitForTime(CelestialBody target, Vector3d launchPos, double delayTime)
        {
            // Form a plane with the launch site, moon and earth centre in, use this as the orbital plane for launch
            CelestialBody mainBody = target.referenceBody;
            Vector3d EarthPos = mainBody.position;
            Vector3d EarthAxis = mainBody.angularVelocity.normalized;

            double targetTime = Planetarium.GetUniversalTime() + flightTime * 24d * 3600d + delayTime;
            Vector3d targetPos = target.getPositionAtUT(targetTime);

            Vector3d upVector = QuaternionD.AngleAxis(delayTime * 360d / mainBody.rotationPeriod, EarthAxis) * (launchPos - EarthPos).normalized;

            Vector3d orbitNorm = Vector3d.Cross(targetPos - EarthPos, upVector).normalized;
            double inclination = Math.Acos(Vector3d.Dot(orbitNorm, EarthAxis));
            if (inclination > Math.PI / 2)
            {
                inclination = Math.PI - inclination;
                orbitNorm *= -1; // make sure orbitNorm always points roughly northwards
            }

            // When checking this: remember that Unity (and KSP) use a left-handed coordinate system; therefore, the
            // cross product follows the left-hand rule.
            Vector3d eastVec = Vector3d.Cross(upVector, EarthAxis).normalized;
            Vector3d northVec = Vector3d.Cross(eastVec, upVector).normalized;
            Vector3d launchVec = Vector3d.Cross(upVector, orbitNorm).normalized;

            double azimuth = Math.Acos(Vector3d.Dot(launchVec, northVec));

            return new OrbitData(orbitNorm, inclination * 180d / Math.PI, azimuth * 180d / Math.PI);
        }

        private double EstimateLaunchTime(CelestialBody target, Vector3d launchPos, double latitude, double startTime)
        {
            double targetAz = 90d;
            double t = startTime;
            OrbitData launchOrbit = CalcOrbitForTime(target, launchPos, t);

            if (Math.Abs(latitude) >= target.orbit.inclination)
            {
                // High latitude path - find the next easterly launch to the target
                while (Math.Abs(launchOrbit.azimuth - targetAz) > 0.01d)
                {
                    double tStep = 60d;
                    double margin = 2.0;
                    double wrapMargin = 1.25;

                    OrbitData nextOrbit = CalcOrbitForTime(target, launchPos, t + tStep);

                    double gradient = (nextOrbit.azimuth - launchOrbit.azimuth);

                    if (Math.Abs(gradient) > 100d)
                    {
                        // Wrapping from north to south, short step and recalc.
                        t += tStep;
                    }
                    else if (gradient > 0)
                    {
                        if (launchOrbit.azimuth < targetAz)
                        {
                            t += tStep * (targetAz - launchOrbit.azimuth) / (margin * gradient);
                        }
                        else
                        {
                            t += tStep * (180 - launchOrbit.azimuth) / (wrapMargin * gradient);
                        }
                    }
                    else
                    {
                        if (launchOrbit.azimuth > targetAz)
                        {
                            t += tStep * (targetAz - launchOrbit.azimuth) / (margin * gradient);
                        }
                        else
                        {
                            t += tStep * (0 - launchOrbit.azimuth) / (wrapMargin * gradient);
                        }
                    }

                    launchOrbit = CalcOrbitForTime(target, launchPos, t);
                }
            }
            else
            {
                double tStep = 1200d;
                double startGradient;

                for (; ; )
                {
                    OrbitData nextOrbit = CalcOrbitForTime(target, launchPos, t + tStep);
                    startGradient = (nextOrbit.azimuth - launchOrbit.azimuth);

                    if (Math.Abs(startGradient) < 100)
                    {
                        if (startGradient > 0 && launchOrbit.azimuth < targetAz)
                            break;

                        if (startGradient < 0 && launchOrbit.azimuth > targetAz)
                            break;
                    }

                    t += tStep;
                    launchOrbit = nextOrbit;
                }

                while (tStep >= 1d)
                {
                    OrbitData nextOrbit = CalcOrbitForTime(target, launchPos, t + tStep);

                    double gradient = (nextOrbit.azimuth - launchOrbit.azimuth);

                    if ((gradient < 0) == (startGradient < 0))
                    {
                        t += tStep;
                        launchOrbit = nextOrbit;
                    }
                    else
                    {
                        t = Math.Max(t - tStep, 0d);
                        tStep /= 2;
                        launchOrbit = CalcOrbitForTime(target, launchPos, t);
                    }
                }
            }

            return t;
        }

        private double EstimateFlightTimeAfterTLI(CelestialBody target, double dV, bool movingTarget = true)
        {
            CelestialBody mainBody = target.referenceBody;
            double gravParameter = mainBody.gravParameter;

            // The formulas are from http://www.braeunig.us/space/orbmech.htm

            // Assuming that the TLI is performed from a circular orbit with altitude = tliAltitudeKM
            // Radius of the orbit, including the radius of the Earth
            double r0 = mainBody.Radius + tliAltitudeKM * 1000;

            // Orbital velocity after TLI
            double v0 = Math.Sqrt(gravParameter / r0) + dV;

            // Eccentricity after TLI (not the full formula, this is correct only at the periapsis)
            double e = r0 * v0 * v0 / gravParameter - 1;

            // e == 1 would mean that the orbit is parabolic. No idea which formulas are applicable in this case.
            // But it's so unlikely that I will just cheat and make such orbits slightly hyperbolic.
            if (e == 1)
            {
                // Increase velocity after TLI  by 0.1 m/s
                v0 = v0 + 0.1;
                // Recalculate eccentricity
                e = r0 * v0 * v0 / gravParameter - 1;
            }

            // Semi-major axis after TLI
            double a = 1 / (2 / r0 - v0 * v0 / gravParameter);

            // Altitude of the Moon at the time of the TLI 
            double r1 = 0;

            // The Moon is moving, so we need to know its altitude when the probe arrives
            // For that we need to know the flight time, which is being calculated here in the first place
            // It can be done in two steps: 
            // 1) make a recursive call of EstimateFlightTimeAfterTLI to find out the approximate flight time,
            //    based on the Moon's current altitude
            // 2) use the Moon's altitude at this approximate time for further calculations
            if (movingTarget)
            {
                // This is the "normal" call of EstimateFlightTimeAfterTLI from outside

                // Making the recursive call of EstimateFlightTimeAfterTLI with movingTarget = false
                double approxFlightTime = EstimateFlightTimeAfterTLI(target, dV, false);

                // Altitude of the Moon at the approximate time of the TLI 
                r1 = target.orbit.GetRadiusAtUT(Planetarium.GetUniversalTime() + approxFlightTime);
            }
            else
            {
                // This is the recursive call of EstimateFlightTimeAfterTLI

                // Altitude of the Moon now
                r1 = target.orbit.GetRadiusAtUT(Planetarium.GetUniversalTime());
            }

            // True anomaly when the vessel reaches the altitude of the Moon (r1)
            double trueAnomaly1 = Math.Acos((a * (1 - e * e) - r1) / (e * r1));

            // Time until the vessel reaches the altitude of the Moon (r1)
            double t1 = 0;

            // Elliptic orbit after TLI
            if (e < 1)
            {
                // Eccentric Anomaly when the vessel reaches the altitude of the Moon
                double eccAnomaly1 = Math.Acos((e + Math.Cos(trueAnomaly1)) / (1 + e * Math.Cos(trueAnomaly1)));
                double meanAnomaly1 = eccAnomaly1 - e * Math.Sin(eccAnomaly1);
                t1 = meanAnomaly1 / Math.Sqrt(gravParameter / (a * a * a));
            }

            // Parabolic orbit (e == 1) has been prevented earlier

            // Hyperbolic orbit
            if (e > 1)
            {
                // Hyperbolic Eccentric Anomaly when the vessel reaches the altitude of the Moon
                // Can't use Math.Acosh, it does not seem to work in .NET 4
                double hEccAnomaly1 = Util.Acosh((e + Math.Cos(trueAnomaly1)) / (1 + e * Math.Cos(trueAnomaly1)));

                t1 = Math.Sqrt(((-a) * (-a) * (-a)) / gravParameter) * (e * Math.Sinh(hEccAnomaly1) - hEccAnomaly1);
            }

            return t1;
        }

        private double EstimateFlightTimeBeforeTLI(CelestialBody target, Vector3d launchPos, double delayTime)
        {
            CelestialBody mainBody = target.referenceBody;
            double gravParameter = mainBody.gravParameter;

            // This code is copied from CalcOrbitForTime
            Vector3d EarthPos = mainBody.position;
            Vector3d EarthAxis = mainBody.angularVelocity.normalized;

            double targetTime = Planetarium.GetUniversalTime() + flightTime * 24d * 3600d + delayTime;
            Vector3d targetPos = target.getPositionAtUT(targetTime);

            Vector3d upVector = Quaternion.AngleAxis((float)(delayTime * 360d / mainBody.rotationPeriod), EarthAxis) * (launchPos - EarthPos).normalized;
            // end of copied code

            // TLI takes place at the point of the orbit that is opposite to the future position of the Moon
            Vector3d tliUpVector = (EarthPos - targetPos).normalized;

            // Need to calculate 0-360 degree angle in prograde direction between upVector at launch and at the time of the TLI.
            float rotationAngle;
            Vector3 rotationAxis;
            Quaternion rotationFromLaunchToTLI = new Quaternion();
            rotationFromLaunchToTLI.SetFromToRotation(upVector, tliUpVector);
            rotationFromLaunchToTLI.ToAngleAxis(out rotationAngle, out rotationAxis);
            // Sometimes this rotation is prograde, sometimes retrograde. We need only prograde rotation.
            if (Vector3d.Dot(rotationAxis.normalized, EarthAxis) > 0)
            {
                // rotationAxis is pointing roughly in the direction of Earth rotation axis => rotation is prograde => do nothing
            }
            else
            {
                // rotationAxis is pointing roughly opposite to Earth rotation axis => rotation is retrograde => rotate the other way
                rotationAngle = (-rotationAngle) % 360;
            }

            // The angle is converted to time in orbit
            double orbitRadius = mainBody.Radius + tliAltitudeKM * 1000;
            double orbitPeriod = 2 * Math.PI * Math.Sqrt((orbitRadius * orbitRadius * orbitRadius) / gravParameter);
            double flightTimeBeforeTLI = rotationAngle / 360 * orbitPeriod;

            return flightTimeBeforeTLI;
        }

        private double EstimateDV(CelestialBody target, Vector3d launchPos)
        {
            float dV = float.NaN;

            // Search in this range
            const float minPossibleDV = 3000;
            const float maxPossibleDV = 6000;

            // Current search range, will be gradually narrowed
            float lowerBound = minPossibleDV;
            float upperBound = maxPossibleDV;

            // Max. 16 attempts, then return whatever value was found
            for (int i = 0; i < 16; i++)
            {
                // guess dV
                dV = (lowerBound + upperBound) / 2;

                // calculate flight time for this dV
                double flightTimeAfterTLI = EstimateFlightTimeAfterTLI(target, dV);
                double flightTimeBeforeTLI = EstimateFlightTimeBeforeTLI(target, launchPos, 0d);
                double estimatedFlightTime = flightTimeBeforeTLI + flightTimeAfterTLI;
                double expectedFlightTime = flightTime * (24 * 60 * 60);

                // Debug.Log(i + " " + dV + " " + flightTime + " " + flightTimeBeforeTLI + " " + flightTimeAfterTLI + " " + estimatedFlightTime);

                if (Double.IsNaN(flightTimeAfterTLI))
                {
                    // dV is so low that target is unreachable, set lower bound to current guess and try again
                    lowerBound = dV;
                    continue;
                }
                else if (estimatedFlightTime > (expectedFlightTime + 5))
                {
                    // dV is too low, set lower bound to current guess and try again
                    lowerBound = dV;
                    continue;
                }
                else if (estimatedFlightTime < (expectedFlightTime - 5))
                {
                    // dV is too high, set upper bound to current guess and try again
                    upperBound = dV;
                    continue;
                }
                else
                {
                    // correct flight time with this dV
                    break;
                }
            }

            if (Math.Abs(dV - minPossibleDV) <= 0.001 * Math.Abs(minPossibleDV) || Math.Abs(dV - maxPossibleDV) <= 0.001 * Math.Abs(maxPossibleDV))
            {
                // dV is incorrect, the correct value is outside the initial search range
                dV = float.NaN;
            }

            return dV;
        }


        private string FormatTime(double t)
        {
            t = Math.Round(t);
            int hours = (int)Math.Floor(t / 3600);
            t -= hours * 3600d;
            int minutes = (int)Math.Floor(t / 60);
            t -= minutes * 60d;
            if (hours > 0)
                return $"{hours}h {minutes}m {t:F0}s";
            else if (minutes > 0)
                return $"{minutes}m {t:F0}s";
            return $"{t:F0}s";
        }

        void MakeMainWindow(int id)
        {
            GUILayout.BeginVertical();

            windowTitle = "Lunar Transfer";

            CelestialBody target = FlightGlobals.fetch.bodies.FirstOrDefault(body => body.name.Equals("Moon", StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                GUILayout.Space(4);
                GUILayout.Box("ERROR: Cannot find the Moon", GUILayout.MinWidth(80));
            }
            else
            {

                double latitude = 0d;
                Vector3d launchPos = GetLaunchPos(target.referenceBody, ref latitude);

                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Latitude:", GUILayout.Height(15), GUILayout.ExpandWidth(false));
                GUILayout.Box(new GUIContent(String.Format("{0:0.00}\u00B0", latitude), ""), GUILayout.MinWidth(60), GUILayout.Height(20));
                GUILayout.Space(5);
                bool showInfo_pressed = GUILayout.Button(" ?", GUILayout.Width(20));
                GUILayout.EndHorizontal();

                if (showInfo) GUILayout.Label("Latitude of current launch site", GUILayout.ExpandWidth(true));

                if (showInfo_pressed)
                {
                    showInfo = !showInfo;
                    // Doing this forces the window to be resized
                    // Without it, the window will become bigger when controls expand, but never become smaller again
                    windowRect = new Rect(windowRect.xMin, windowRect.yMin, -1, -1);
                }

                GUILayout.Space(4);
                GUILayout.Label("Flight Time (days)", GUILayout.ExpandWidth(true));
                MakeNumberEditField(ref flightTime, ref nextTickFT, 0.1f, 0.1f);
                var t = TimeSpan.FromDays(flightTime);
                GUILayout.Box(new GUIContent($"{t.Days}d {t.Hours}h {t.Minutes}m {t.Seconds}s", ""), GUILayout.MinWidth(100));
                if (showInfo) GUILayout.Label("Coast duration to the Moon after the maneuver", GUILayout.ExpandWidth(true));

                OrbitData launchOrbit = CalcOrbitForTime(target, launchPos, 0d);
                double firstLaunchETA = EstimateLaunchTime(target, launchPos, latitude, 0d);
                double secondLaunchETA = EstimateLaunchTime(target, launchPos, latitude, firstLaunchETA + 3600d);

                double dV = EstimateDV(target, launchPos);

                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Required \u0394V", GUILayout.ExpandWidth(true));
                bool showAltitude_pressed = GUILayout.Button("...", GUILayout.Width(30));
                GUILayout.EndHorizontal();

                GUILayout.Box(new GUIContent(String.Format("{0:0.00 m/s}", dV), ""), GUILayout.MinWidth(100));
                if (showInfo) GUILayout.Label("Required change in velocity for the maneuver if launched now", GUILayout.ExpandWidth(true));

                if (showAltitude_pressed)
                {
                    showAltitude = !showAltitude;
                    // Doing this forces the window to be resized
                    // Without it, the window will become bigger when controls expand, but never become smaller again
                    windowRect = new Rect(windowRect.xMin, windowRect.yMin, -1, -1);
                }

                if (showAltitude)
                {
                    GUILayout.Label("Parking Orbit (km)", GUILayout.ExpandWidth(true));
                    MakeNumberEditField(ref tliAltitudeKM, ref nextTickTA, 5.0f, 140.0f);
                    if (showInfo) GUILayout.Label("Planned altitude of the circular parking orbit before the maneuver", GUILayout.ExpandWidth(true));
                }

                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Launch Now Incl.", GUILayout.ExpandWidth(true));
                bool showParking0_pressed = GUILayout.Button("...", GUILayout.Width(30));
                GUILayout.EndHorizontal();

                GUILayout.Space(4);
                GUILayout.Box(new GUIContent($"{(launchOrbit.azimuth > 90d ? -launchOrbit.inclination : launchOrbit.inclination):F2}\u00B0",""), GUILayout.MinWidth(100));
                if (showInfo) GUILayout.Label("Launch to this inclination now to get into the right parking orbit", GUILayout.ExpandWidth(true));

                string tooltip = Math.Abs(latitude) >= target.orbit.inclination ?
                    "Launch Easterly at this time to get into the right parking orbit" :
                    "Launch at this time at this inclination to get into the right parking orbit";

                if (showParking0_pressed)
                {
                    showParking0 = !showParking0;
                    // Doing this forces the window to be resized
                    // Without it, the window will become bigger when controls expand, but never become smaller again
                    windowRect = new Rect(windowRect.xMin, windowRect.yMin, -1, -1);
                }

                if (showParking0)
                {
                    double timeInOrbit0 = EstimateFlightTimeBeforeTLI(target, launchPos, 0d);
                    GUILayout.Label("Time in parking orbit", GUILayout.ExpandWidth(true));
                    GUILayout.Box(new GUIContent(FormatTime(timeInOrbit0), ""), GUILayout.MinWidth(100));
                }

                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                GUILayout.Label("First Window", GUILayout.ExpandWidth(true));
                bool showParking1_pressed = GUILayout.Button("...", GUILayout.Width(30));
                GUILayout.EndHorizontal();

                GUILayout.Space(4);
                GUILayout.Box(new GUIContent(FormatTime(firstLaunchETA), ""), GUILayout.MinWidth(100));
                if (showInfo) GUILayout.Label(tooltip, GUILayout.ExpandWidth(true));

                if (showParking1_pressed)
                {
                    showParking1 = !showParking1;
                    // Doing this forces the window to be resized
                    // Without it, the window will become bigger when controls expand, but never become smaller again
                    windowRect = new Rect(windowRect.xMin, windowRect.yMin, -1, -1);
                }

                if (showParking1)
                {
                    double timeInOrbit1 = EstimateFlightTimeBeforeTLI(target, launchPos, firstLaunchETA);
                    GUILayout.Label("Time in parking orbit", GUILayout.ExpandWidth(true));
                    GUILayout.Box(new GUIContent(FormatTime(timeInOrbit1), ""), GUILayout.MinWidth(100));
                }

                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Second Window", GUILayout.ExpandWidth(true));
                bool showParking2_pressed = GUILayout.Button("...", GUILayout.Width(30));
                GUILayout.EndHorizontal();

                GUILayout.Space(4);
                GUILayout.Box(new GUIContent(FormatTime(secondLaunchETA), ""), GUILayout.MinWidth(100));
                if (showInfo) GUILayout.Label(tooltip, GUILayout.ExpandWidth(true));

                if (showParking2_pressed)
                {
                    showParking2 = !showParking2;
                    // Doing this forces the window to be resized
                    // Without it, the window will become bigger when controls expand, but never become smaller again
                    windowRect = new Rect(windowRect.xMin, windowRect.yMin, -1, -1);
                }

                if (showParking2)
                {
                    double timeInOrbit2 = EstimateFlightTimeBeforeTLI(target, launchPos, secondLaunchETA);
                    GUILayout.Label("Time in parking orbit", GUILayout.ExpandWidth(true));
                    GUILayout.Box(new GUIContent(FormatTime(timeInOrbit2), ""));
                }

                GUILayout.Space(4);
                GUILayout.Label("Warp Margin (sec)", GUILayout.ExpandWidth(true));
                MakeNumberEditField(ref warpMargin, ref nextTickWM, 5f, 0f);

                GUILayout.Space(8);
                GUILayout.BeginHorizontal();
                GUI.enabled = KACWrapper.APIReady;
                bool addAlarm = GUILayout.Button("Add Alarm", GUILayout.MinWidth(80));
                GUI.enabled = true;
                bool toggleWarp = GUILayout.Button(TimeWarp.CurrentRate > 1f ? "Stop Warp" : "Warp", GUILayout.MinWidth(80));
                GUILayout.EndHorizontal();

                if (addAlarm)
                {
                    if (KACWrapper.APIReady)
                    {
                        string alarmId = KACWrapper.KAC.CreateAlarm(KACWrapper.KACAPI.AlarmTypeEnum.Raw, "Lunar transfer window", Planetarium.GetUniversalTime() + firstLaunchETA - warpMargin);
                        if (!string.IsNullOrEmpty(alarmId))
                        {
                            //if the alarm was made get the object so we can update it
                            KACWrapper.KACAPI.KACAlarm alarm = KACWrapper.KAC.Alarms.First(z => z.ID == alarmId);

                            //Now update some of the other properties
                            alarm.AlarmAction = KACWrapper.KACAPI.AlarmActionEnum.KillWarp;
                        }
                    }
                }

                if (toggleWarp)
                {
                    if (TimeWarp.CurrentRate > 1f)
                    {
                        TimeWarp.fetch.CancelAutoWarp();
                        TimeWarp.SetRate(0, false);
                    }
                    else
                    {
                        TimeWarp.fetch.WarpTo(Planetarium.GetUniversalTime() + firstLaunchETA - warpMargin);
                    }
                }
            }

            GUILayout.EndVertical();
            GUI.DragWindow();
        }
    }
}
