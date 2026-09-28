using System.Text.Json;
using SolidWorksWorker.Features;

// 只调用生产代码原有算法，诊断辅助工具不另造指纹规则。
Console.WriteLine(JsonSerializer.Serialize(new
{
    feature = FeatureExecutionEvidencePolicy.ComputeCurrentSourceRevision(),
    v20d = V20DThreeCircleCutEvidencePolicy.ComputeCurrentSourceRevision()
}));
