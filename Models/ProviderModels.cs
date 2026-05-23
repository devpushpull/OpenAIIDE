namespace AIIDEWPF.Models;

public class ProviderDef
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public List<ModelDef> Models { get; set; } = new();
    /// <summary>是否为用户自定义提供商（非内置）</summary>
    public bool IsCustom { get; set; } = false;
    public override string ToString() => Name;
}

/// <summary>模型能力层级（用于智能调度）</summary>
public enum ModelTier
{
    /// <summary>L1 轻量/经济：简单问答、代码解释、单文件小改</summary>
    Budget = 1,
    /// <summary>L2 标准：中等复杂任务、多文件修改、搜索重构</summary>
    Standard = 2,
    /// <summary>L3 旗舰/推理：复杂架构设计、多文件大改、深度推理</summary>
    Premium = 3
}

public class ModelDef
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool AgentMode { get; set; } = true;
    public bool QAMode { get; set; } = true;
    /// <summary>最大输出令牌数（0=使用模型默认值）</summary>
    public int MaxTokens { get; set; } = 0;
    /// <summary>是否为用户自定义模型（非内置）</summary>
    public bool IsCustom { get; set; } = false;
    /// <summary>模型能力层级（用于智能调度路由）</summary>
    public ModelTier Tier { get; set; } = ModelTier.Standard;
    public override string ToString() => Name;
}

public class ChatModeDef
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public bool AllowsFileWrite { get; set; } = true;
    public bool AllowsTerminal { get; set; } = true;
    public bool AllowsWebSearch { get; set; } = true;
    public bool RequiresApproval { get; set; }
    public string SystemPromptSuffix { get; set; } = string.Empty;
    public override string ToString() => Name;
}
