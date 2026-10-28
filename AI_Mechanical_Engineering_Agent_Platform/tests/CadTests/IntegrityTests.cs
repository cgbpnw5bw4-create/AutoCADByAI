using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using CadKernel;
using Xunit;

namespace CadTests
{
    /// <summary>重建校核读取失败必须登记问题; 仅注入纯逻辑委托, 不连接 SolidWorks。</summary>
    public class IntegrityTests
    {
        [Fact]
        public void ZeroRebuildIssuesDoesNotReadDetails()
        {
            var issues = new List<string>();
            Integrity.CheckRebuild(() => 0, () => throw new InvalidOperationException("不应读取详情"), issues);
            Assert.Empty(issues);
        }

        [Fact]
        public void RebuildIssuesIncludeCountAndFeatureDetails()
        {
            var issues = new List<string>();
            Integrity.CheckRebuild(() => 2, () => "拉伸1[1], 切除1[2](警告)", issues);
            Assert.Equal("重建错误/警告 2: 拉伸1[1], 切除1[2](警告)", Assert.Single(issues));
        }

        [Fact]
        public void CountReadComFailureIsAnIssueInsteadOfZeroErrors()
        {
            var issues = new List<string>();
            bool detailsRead = false;
            Integrity.CheckRebuild(() => throw new COMException("GetWhatsWrongCount 无法读取", unchecked((int)0x80004005)),
                () => { detailsRead = true; return ""; }, issues);
            Assert.False(detailsRead);
            Assert.Equal("重建检查失败: GetWhatsWrongCount 无法读取", Assert.Single(issues));
        }

        [Fact]
        public void DetailReadComFailureStillReportsFailedCheck()
        {
            var issues = new List<string> { "引用文件不存在" };
            Integrity.CheckRebuild(() => 1, () => throw new COMException("GetWhatsWrong 无法读取"), issues);
            Assert.Equal(2, issues.Count);
            Assert.Equal("引用文件不存在", issues[0]);
            Assert.Equal("重建检查失败: GetWhatsWrong 无法读取", issues[1]);
        }
    }
}
