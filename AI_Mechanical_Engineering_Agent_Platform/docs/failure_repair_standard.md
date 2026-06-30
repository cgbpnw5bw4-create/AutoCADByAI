# 失败修复总规范

失败不能只停在 `Failed`。任何失败都必须变成可行动信息。

## 统一失败处理流程

1. 读取最新 report。
2. 判断 `failure_stage`。
3. 归类失败类型。
4. 检查是否已有对应 repair playbook。
5. 如果有，按 playbook 修复。
6. 如果没有，生成新的 failure analysis。
7. 查证官方 API、本地文档和参考资料。
8. 生成 evidence report。
9. 提出候选修复策略。
10. 只做最小修复。
11. 重新运行 diagnostic runner。
12. 成功后回填主 Worker。
13. 更新说明文件。
14. 更新 self-check。
15. 更新测试。

## CAD 与 API 规则

API 失败必须进入 API Evidence Driven Repair Loop。真实 CAD 失败必须先在诊断 Runner 中隔离验证，再回填 Worker。不能凭感觉改长参数 COM 调用，不能复制第三方脚本，不能把失败伪装成成功。
