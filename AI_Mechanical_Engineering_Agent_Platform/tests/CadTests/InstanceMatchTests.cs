using System.Collections.Generic;
using CadKernel;
using CadProjects.Imported;
using Xunit;

namespace CadTests
{
    /// <summary>实例位置核对的纯变换匹配, 不读取 CAD 文件。</summary>
    public sealed class InstanceMatchTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SameOriginInstancesMatchTheirOwnRotationRegardlessOfOrder(bool reverse)
        {
            var expected = new List<Xf> { Xf.RotZ(0, 1, 2, 3), Xf.RotZ(90, 1, 2, 3) };
            if (reverse) expected.Reverse();
            foreach (double angle in new[] { 90.0, 0 })
            {
                int i = ImportedAssembly.MatchInstance(Xf.RotZ(angle, 1, 2, 3), expected, out double dt, out double dr);
                Assert.InRange(i, 0, expected.Count - 1);
                Assert.Equal(0, dt);
                Assert.Equal(0, dr);
                expected.RemoveAt(i);
            }
            Assert.Empty(expected);
        }

        [Fact]
        public void ValidPoseWinsOverCloserCandidateWithWrongRotation()
        {
            var expected = new List<Xf> { Xf.RotZ(90, 0, 0, 0), Xf.T(0.005, 0, 0) };
            Assert.Equal(1, ImportedAssembly.MatchInstance(Xf.T(0, 0, 0), expected, out double dt, out double dr));
            Assert.Equal(0.005, dt, 9);
            Assert.Equal(0, dr);
            Assert.Equal(2, expected.Count); // 匹配本身不消费实例。
        }

        [Theory]
        [InlineData(0, 90)]
        [InlineData(0.02, 0)]
        public void PositionAndRotationMustBothMeetTheExistingTolerances(double x, double angle)
        {
            var expected = new List<Xf> { Xf.RotZ(angle, x, 0, 0) };
            Assert.Equal(-1, ImportedAssembly.MatchInstance(Xf.T(0, 0, 0), expected, out double dt, out double dr));
            Assert.True(dt > 0.01 || dr > 1e-6);
            Assert.Single(expected);
        }

        [Fact]
        public void NoRemainingInstanceCannotMatch()
        {
            Assert.Equal(-1, ImportedAssembly.MatchInstance(Xf.T(0, 0, 0), new List<Xf>(), out _, out _));
        }
    }
}
