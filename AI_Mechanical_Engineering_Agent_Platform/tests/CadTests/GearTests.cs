using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CadLibrary;
using Xunit;

namespace CadTests
{
    /// <summary>渐开线齿廓 C# 移植与原 Python (已在 SolidWorks 实测) 逐段一致</summary>
    public class GearTests
    {
        static JsonElement Ref(string name)
        {
            var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "gear_geom_reference.json")));
            return doc.RootElement.GetProperty("profiles").GetProperty(name);
        }

        static void Same(double[] a, JsonElement b)
        {
            Assert.Equal(b[0].GetDouble(), a[0], 9);
            Assert.Equal(b[1].GetDouble(), a[1], 9);
        }

        static void Compare(List<GearSeg> segs, GearInfo info, JsonElement r)
        {
            var rs = r.GetProperty("segs").EnumerateArray().ToList();
            Assert.Equal(rs.Count, segs.Count);
            for (int i = 0; i < segs.Count; i++)
            {
                var g = segs[i]; var e = rs[i];
                Assert.Equal(e.GetProperty("k").GetString()[0], g.Kind);
                if (g.Kind == 'S')
                {
                    var pts = e.GetProperty("pts").EnumerateArray().ToList();
                    Assert.Equal(pts.Count, g.Pts.Count);
                    for (int j = 0; j < pts.Count; j++) Same(g.Pts[j], pts[j]);
                }
                else
                {
                    Same(g.P1, e.GetProperty("p1"));
                    Same(g.P2, e.GetProperty("p2"));
                    if (g.Kind == 'A')
                    {
                        Same(g.C, e.GetProperty("c"));
                        Assert.Equal(e.GetProperty("dir").GetInt32(), g.Dir);
                    }
                }
            }
            var ri = r.GetProperty("info");
            Assert.Equal(ri.GetProperty("ra").GetDouble(), info.Ra, 9);
            Assert.Equal(ri.GetProperty("rf").GetDouble(), info.Rf, 9);
            Assert.Equal(ri.GetProperty("rb").GetDouble(), info.Rb, 9);
        }

        [Fact]
        public void SunPlanetRingMatchPythonReference()
        {
            GearInfo gi;
            Compare(Gears.External(18, 2.0, out gi), gi, Ref("sun"));
            Compare(Gears.External(27, 2.0, out gi), gi, Ref("planet"));
            Compare(Gears.Internal(72, 2.0, out gi), gi, Ref("ring"));
        }

        [Theory]
        [InlineData(18, 2.0)]
        [InlineData(27, 2.0)]
        [InlineData(42, 1.5)]   // 齿根圆 > 基圆的分支
        public void ExternalProfileIsClosed(int z, double m)
        {
            GearInfo gi;
            Assert.True(Gears.MaxGap(Gears.External(z, m, out gi)) < 1e-9);
        }

        [Fact]
        public void InternalProfileIsClosed()
        {
            GearInfo gi;
            Assert.True(Gears.MaxGap(Gears.Internal(72, 2.0, out gi)) < 1e-9);
            Assert.Equal(70.0, gi.Ra, 9);    // 内齿顶圆 Ø140
            Assert.Equal(74.5, gi.Rf, 9);    // 齿根圆 Ø149
        }

        [Fact]
        public void PlanetaryConditionsAcceptReferenceAndRejectViolations()
        {
            Assert.Empty(Gears.PlanetaryConditions(18, 27, 72, 3));
            Assert.Contains(Gears.PlanetaryConditions(18, 27, 70, 3), s => s.Contains("同心"));
            Assert.Contains(Gears.PlanetaryConditions(17, 27, 71, 3), s => s.Contains("装配"));
            Assert.Contains(Gears.PlanetaryConditions(18, 36, 90, 6), s => s.Contains("邻接"));
        }

        [Fact]
        public void RootFilletLimitIsReportedBeforeModelling()
        {
            // 与原 Python 实现逐一扫描结果一致: z=30~41 不适用
            for (int z = 12; z < 80; z++)
                Assert.Equal(z >= 30 && z <= 41, Gears.ExternalApplicable(z) != null);
            Assert.Contains(Gears.PlanetaryConditions(20, 40, 100, 4), s => s.Contains("z=40"));
        }
    }
}
