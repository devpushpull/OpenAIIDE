using System.Text.RegularExpressions;
using AIIDEWPF.Models;

namespace AIIDEWPF.Services;

/// <summary>
/// 大模型智能调度路由器 —— 根据任务难度自动选择最合适的模型，
/// 避免简单任务浪费高端模型用量，同时确保复杂任务获得最强能力。
/// </summary>
public class ModelRouterService
{
    private readonly ModelManager _modelManager;

    /// <summary>路由结果</summary>
    public record RouteResult(
        string ProviderId,
        string ModelId,
        string ModelName,
        ModelTier SelectedTier,
        ModelTier DetectedDifficulty,
        string Reason);

    public ModelRouterService(ModelManager modelManager)
    {
        _modelManager = modelManager;
    }

    /// <summary>
    /// 分析用户消息，返回最适合的模型路由。
    /// 优先在用户当前激活的提供商内选择，若无合适模型则跨提供商查找。
    /// </summary>
    public RouteResult Route(string userMessage, string? currentProviderId = null, string? currentModelId = null)
    {
        var difficulty = AnalyzeTaskDifficulty(userMessage);
        var preferredProviderId = currentProviderId ?? _modelManager.ActiveProvider?.Id;

        // 策略：按难度层级从低到高尝试匹配
        // - Budget 任务：优先 L1，无则 L2，最后 L3
        // - Standard 任务：优先 L2，无则 L3（避免降级到 L1 导致质量差）
        // - Premium 任务：只用 L3，无则 L2
        var candidates = GetAllCandidates(preferredProviderId);

        ModelDef? selected = null;
        string? selectedProviderId = null;
        string reason = "";

        switch (difficulty)
        {
            case ModelTier.Budget:
                // 简单任务：Budget > Standard > Premium (省用量优先)
                selected = FindInTier(candidates, ModelTier.Budget)
                    ?? FindInTier(candidates, ModelTier.Standard)
                    ?? FindInTier(candidates, ModelTier.Premium);
                reason = selected?.Tier == ModelTier.Budget
                    ? "简单任务 → 使用经济模型节省用量"
                    : "简单任务 → 无合适经济模型，使用标准模型";
                break;

            case ModelTier.Standard:
                // 标准任务：Standard > Premium > Budget
                selected = FindInTier(candidates, ModelTier.Standard)
                    ?? FindInTier(candidates, ModelTier.Premium)
                    ?? FindInTier(candidates, ModelTier.Budget);
                reason = selected?.Tier == ModelTier.Standard
                    ? "标准任务 → 使用标准模型"
                    : selected?.Tier == ModelTier.Premium
                        ? "标准任务 → 无标准模型，使用旗舰模型（已配置API Key）"
                        : "标准任务 → 降级使用经济模型";
                break;

            case ModelTier.Premium:
                // 复杂任务：Premium ONLY，实在没有才 Standard
                selected = FindInTier(candidates, ModelTier.Premium)
                    ?? FindInTier(candidates, ModelTier.Standard);
                reason = selected?.Tier == ModelTier.Premium
                    ? "复杂任务 → 使用旗舰模型确保质量"
                    : "复杂任务 → 无旗舰模型可用，降级使用标准模型";
                break;
        }

        // 最终兜底：使用当前激活模型
        if (selected == null)
        {
            var active = _modelManager.ActiveModel;
            var activeProviderId = _modelManager.ActiveProvider?.Id ?? "deepseek";
            if (active != null)
            {
                selected = active;
                selectedProviderId = activeProviderId;
                reason = "未找到合适路由 → 使用当前激活模型";
            }
            else
            {
                // 硬兜底
                selectedProviderId = "deepseek";
                selected = _modelManager.GetModelsFor("deepseek").FirstOrDefault()
                    ?? new ModelDef { Id = "deepseek-v4-pro", Name = "DeepSeek V4 Pro" };
                reason = "兜底 → 使用 DeepSeek V4 Pro";
            }
        }

        if (selectedProviderId == null)
            selectedProviderId = candidates.FirstOrDefault(c => c.model.Id == selected.Id).providerId
                ?? preferredProviderId ?? "deepseek";

        return new RouteResult(
            selectedProviderId,
            selected.Id,
            selected.Name,
            selected.Tier,
            difficulty,
            reason);
    }

    /// <summary>带日志的路由方法（用于生产环境）</summary>
    public RouteResult RouteWithLogging(string userMessage, string? currentProviderId = null, string? currentModelId = null)
    {
        var result = Route(userMessage, currentProviderId, currentModelId);
        LogService.Instance.Info(
            $"模型路由: {result.SelectedTier} → {result.ModelName} (原因: {result.Reason})",
            "ModelRouter");
        return result;
    }

    /// <summary>
    /// 获取指定提供商是否有某个层级的API Key已配置
    /// </summary>
    public bool HasApiKeyForProvider(string providerId)
    {
        var key = _modelManager.GetEffectiveApiKey(providerId);
        return !string.IsNullOrEmpty(key);
    }

    // ========== 私有方法 ==========

    private List<(string providerId, ModelDef model)> GetAllCandidates(string? preferredProviderId)
    {
        var result = new List<(string providerId, ModelDef model)>();

        foreach (var provider in _modelManager.GetProviders())
        {
            // 跳过未配置 API Key 的提供商
            var apiKey = _modelManager.GetEffectiveApiKey(provider.Id);
            if (string.IsNullOrEmpty(apiKey)) continue;

            foreach (var model in provider.Models)
            {
                // 只考虑支持智能体模式的模型
                if (!model.AgentMode) continue;

                // 优先提供商排在前面
                if (provider.Id == preferredProviderId)
                    result.Insert(0, (provider.Id, model));
                else
                    result.Add((provider.Id, model));
            }
        }

        return result;
    }

    private static ModelDef? FindInTier(List<(string providerId, ModelDef model)> candidates, ModelTier tier)
    {
        foreach (var (_, model) in candidates)
        {
            if (model.Tier == tier)
                return model;
        }
        return null;
    }

    /// <summary>
    /// 分析用户消息的任务难度。
    /// 基于关键词、消息长度、是否涉及文件操作/架构设计等综合判断。
    /// </summary>
    public static ModelTier AnalyzeTaskDifficulty(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return ModelTier.Budget;

        var lower = message.ToLowerInvariant();
        var msgLen = message.Length;
        int score = 0;

        // === 复杂任务信号（+分 → Premium）===
        // 架构/设计
        if (Regex.IsMatch(lower, @"架构|重构|refactor|architecture|design\s+pattern|系统设计|整体|方案"))
            score += 3;
        // 多文件操作
        if (Regex.IsMatch(lower, @"多.*文件|批量|全部|所有.*文件|整个项目|throughout|across.*files"))
            score += 3;
        // 复杂工具链
        if (Regex.IsMatch(lower, @"数据库|database|迁移|migration|部署|deploy|docker|k8s|kubernetes|ci/cd"))
            score += 3;
        // 算法实现
        if (Regex.IsMatch(lower, @"算法|algorithm|实现.*功能|编写.*完整|从零.*构建|build.*from.*scratch"))
            score += 2;
        // 新功能开发
        if (Regex.IsMatch(lower, @"新增|创建.*功能|开发|develop|implement.*feature|添加.*模块"))
            score += 2;
        // 调试复杂问题
        if (Regex.IsMatch(lower, @"为什么.*不行|排查|debug.*complex|堆栈|stack\s*trace|内存泄漏|memory\s*leak|性能.*问题|perf"))
            score += 2;
        // 长消息 + 多步骤
        if (msgLen > 500) score += 2;
        if (msgLen > 1000) score += 1;
        // 多个问号/步骤
        var stepCount = Regex.Matches(lower, @"\d+\.\s|步骤|step\s*\d|首先.*然后|first.*then").Count;
        if (stepCount >= 3) score += 2;
        else if (stepCount >= 1) score += 1;

        // === 简单任务信号（-分 → Budget）===
        // 简单问答
        if (Regex.IsMatch(lower, @"^(什么|什么是|怎么|如何|what|how|why|谁|which)\b") && msgLen < 200)
            score -= 2;
        // 代码解释
        if (Regex.IsMatch(lower, @"解释|explain|这段代码|这个函数|这个类|什么意思") && msgLen < 300)
            score -= 2;
        // 单文件小改
        if (Regex.IsMatch(lower, @"修改.*这个|改.*一下|修改.*一行|加.*注释|改.*名字|rename") && msgLen < 400)
            score -= 1;
        // 查找/搜索
        if (Regex.IsMatch(lower, @"查找|搜索|找到|在哪里|定位|search|find|locate|grep") && msgLen < 200)
            score -= 2;
        // 短小精悍
        if (msgLen < 100) score -= 1;
        if (msgLen < 50) score -= 1;

        // === 判定 ===
        if (score >= 5) return ModelTier.Premium;
        if (score >= 2) return ModelTier.Standard;
        return ModelTier.Budget;
    }
}
