namespace AIIDEWPF.Models;

/// <summary>钩子配置 —— 在 AI 操作生命周期的关键节点执行自定义脚本</summary>
public class HookConfig
{
    /// <summary>钩子唯一标识</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>触发事件: pre_tool / post_tool / pre_ai_call / post_ai_call / on_file_change</summary>
    public string Event { get; set; } = string.Empty;

    /// <summary>要执行的命令（支持占位符: {FILE}, {TOOL}, {ARGS}, {PROJECT}）</summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>是否启用</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>超时时间（秒），默认30秒</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>是否阻断主流程（钩子失败时阻止操作继续）</summary>
    public bool BlockOnFailure { get; set; } = false;

    /// <summary>描述</summary>
    public string Description { get; set; } = string.Empty;
}
