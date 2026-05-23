using System.IO;
using System.Text.Json;
using AIIDEWPF.Models;

namespace AIIDEWPF.Services;

/// <summary>
/// 大模型注册管理中心 — 管理接入的AI提供商、模型、模式切换
/// </summary>
public class ModelManager
{
    private readonly ConfigService _config;
    private Dictionary<string, ProviderDef> _providers = new();

    /// <summary>全局模型数量上限（防止过多模型导致性能下降）</summary>
    public const int MaxTotalModels = 100;

    /// <summary>当前已注册的模型总数（全部提供商合计）</summary>
    public int TotalModelCount => _providers.Values.Sum(p => p.Models.Count);

    /// <summary>是否还可以添加更多模型</summary>
    public bool CanAddMoreModels => TotalModelCount < MaxTotalModels;

    /// <summary>已使用的模型配额描述</summary>
    public string ModelQuotaDescription => $"{TotalModelCount} / {MaxTotalModels} 个模型";

    /// <summary>内置提供商数量</summary>
    public int BuiltInProviderCount => _providers.Count(p => !p.Value.IsCustom);

    /// <summary>自定义提供商数量</summary>
    public int CustomProviderCount => _providers.Count(p => p.Value.IsCustom);

    public ProviderDef? ActiveProvider { get; private set; }
    public ModelDef? ActiveModel { get; private set; }

    public string ActiveMode { get; set; } = "agent"; // agent / qa
    public bool IsQAMode => ActiveMode == "qa";

    public ModelManager(ConfigService config)
    {
        _config = config;
        EnsureBuiltInProviders();
        LoadFromConfig();
    }

    // ===== Provider 管理 =====

    public IEnumerable<ProviderDef> GetProviders() => _providers.Values;

    public ProviderDef? GetProvider(string id) =>
        _providers.TryGetValue(id, out var p) ? p : null;

    public IEnumerable<ModelDef> GetModelsFor(string providerId) =>
        _providers.TryGetValue(providerId, out var p) ? p.Models : Enumerable.Empty<ModelDef>();

    public string? GetBaseUrl(string providerId) =>
        _providers.TryGetValue(providerId, out var p) ? p.BaseUrl : null;

    public string? GetEffectiveApiKey(string providerId)
    {
        if (_providers.TryGetValue(providerId, out var p) && !string.IsNullOrEmpty(p.ApiKey))
            return p.ApiKey;
        return _config.GetAIConfig().ApiKey;
    }

    /// <summary>检查是否至少有一个提供商或全局已填入 API Key</summary>
    public bool HasAnyApiKeyConfigured()
    {
        // 检查全局 API Key 是否已填入
        if (!string.IsNullOrEmpty(_config.GetAIConfig().ApiKey))
            return true;
        // 检查各提供商的 API Key 是否已填入
        foreach (var provider in _providers.Values)
        {
            if (!string.IsNullOrEmpty(provider.ApiKey))
                return true;
        }
        return false;
    }

    // ===== 模式支持（核心逻辑由 ChatModeService 管理） =====

    /// <summary>获取当前模型支持的模式（内部使用，供 ChatModeService 委托）</summary>
    public ChatModeDef[] GetAvailableModes()
    {
        if (ActiveModel == null) return new ChatModeDef[] {
            new() { Id = "agent", Name = "智能体", Description = "可读/写/改/删项目文件，执行终端命令" },
            new() { Id = "qa", Name = "问答", Description = "仅扫描/阅读项目代码回答问题，不修改任何文件" }
        };
        var modes = new List<ChatModeDef>();
        if (ActiveModel.AgentMode) modes.Add(new() { Id = "agent", Name = "智能体", Description = "可读/写/改/删项目文件，执行终端命令" });
        if (ActiveModel.QAMode) modes.Add(new() { Id = "qa", Name = "问答", Description = "仅扫描/阅读项目代码回答问题，不修改任何文件" });
        return modes.ToArray();
    }

    // ===== 激活配置 =====

    public void SetActive(string providerId, string modelId)
    {
        if (!_providers.TryGetValue(providerId, out var provider))
            throw new InvalidOperationException($"未知提供商: {providerId}");

        var model = provider.Models.FirstOrDefault(m => m.Id == modelId);
        if (model == null)
            throw new InvalidOperationException($"未知模型: {providerId}/{modelId}");

        ActiveProvider = provider;
        ActiveModel = model;

        // 如果当前模式不被新模型支持，自动切到支持的
        var modes = GetAvailableModes();
        if (!modes.Any(m => m.Id == ActiveMode))
            ActiveMode = modes[0].Id;

        // 持久化
        _config.SetAIProvider(providerId);
        _config.SetAIModel(modelId);

        LogService.Instance.Info($"模型切换: {provider.Name}/{model.Name} (模式: {ActiveMode})", "ModelManager");
    }

    public void RegisterProvider(ProviderDef def)
    {
        if (!CanAddMoreModels && !_providers.ContainsKey(def.Id))
            throw new InvalidOperationException($"已达到模型数量上限（{MaxTotalModels} 个），请先删除不常用的模型再添加。");
        _providers[def.Id] = def;
        var cfg = _config.GetConfig();
        cfg.Providers[def.Id] = def;
    }

    /// <summary>注册或更新提供商和模型</summary>
    public ProviderDef RegisterOrUpdate(string id, string name, string baseUrl, string apiKey,
        string modelId, string modelName, bool agent = true, bool qa = true, int maxTokens = 0, bool isCustom = false)
    {
        if (!_providers.TryGetValue(id, out var provider))
        {
            // 新增提供商时检查上限
            if (!CanAddMoreModels)
                throw new InvalidOperationException($"已达到模型数量上限（{MaxTotalModels} 个），请先删除不常用的模型再添加。");
            provider = new ProviderDef { Id = id, IsCustom = isCustom };
            _providers[id] = provider;
        }
        provider.Name = name;
        provider.BaseUrl = baseUrl;
        if (!string.IsNullOrEmpty(apiKey))
            provider.ApiKey = apiKey;

        var model = provider.Models.FirstOrDefault(m => m.Id == modelId);
        if (model == null)
        {
            // 新增模型时检查上限
            if (!CanAddMoreModels)
                throw new InvalidOperationException($"已达到模型数量上限（{MaxTotalModels} 个），请先删除不常用的模型再添加。");
            model = new ModelDef { Id = modelId, IsCustom = isCustom };
            provider.Models.Add(model);
        }
        model.Name = modelName;
        model.AgentMode = agent;
        model.QAMode = qa;
        if (maxTokens > 0)
            model.MaxTokens = maxTokens;

        var cfg = _config.GetConfig();
        cfg.Providers[id] = provider;
        return provider;
    }

    /// <summary>删除整个提供商及其所有模型</summary>
    public bool RemoveProvider(string providerId)
    {
        if (!_providers.TryGetValue(providerId, out var prov))
            return false;
        // 不允许删除内置提供商
        if (!prov.IsCustom)
            return false;
        _providers.Remove(providerId);
        var cfg = _config.GetConfig();
        cfg.Providers.Remove(providerId);
        return true;
    }

    /// <summary>删除某个模型（若提供商用光则一并删除提供商）。不允许删除内置模型。</summary>
    public bool RemoveModel(string providerId, string modelId)
    {
        if (!_providers.TryGetValue(providerId, out var prov))
            return false;
        // 不允许删除内置提供商的模型
        if (!prov.IsCustom)
            return false;
        var removed = prov.Models.RemoveAll(m => m.Id == modelId) > 0;
        if (prov.Models.Count == 0)
            _providers.Remove(providerId);
        // 持久化
        var cfg = _config.GetConfig();
        if (_providers.TryGetValue(providerId, out var p))
            cfg.Providers[providerId] = p;
        else
            cfg.Providers.Remove(providerId);
        return removed;
    }

    /// <summary>根据模型 ID 返回官方默认最大输出令牌数</summary>
    public string GetDefaultMaxTokens(string modelId)
    {
        var lowered = modelId.ToLowerInvariant();
        // DeepSeek 全系 384K
        if (lowered.Contains("deepseek")) return "384000";
        // OpenAI GPT-5 系列 128K
        if (lowered.Contains("gpt-5")) return "131072";
        if (lowered.Contains("gpt-4.1")) return "32768";
        if (lowered.Contains("gpt-4") || lowered.Contains("gpt-4o")) return "16384";
        if (lowered.Contains("o4-mini")) return "16384";
        // Anthropic Claude
        if (lowered.Contains("claude-opus")) return "64000";
        if (lowered.Contains("claude-sonnet") || lowered.Contains("claude-haiku")) return "64000";
        if (lowered.Contains("claude")) return "64000";
        // Google Gemini
        if (lowered.Contains("gemini-3")) return "65536";
        if (lowered.Contains("gemini-2.5")) return "65536";
        if (lowered.Contains("gemini")) return "65536";
        // xAI Grok
        if (lowered.Contains("grok-4")) return "32768";
        if (lowered.Contains("grok")) return "32768";
        // Mistral
        if (lowered.Contains("mistral-large")) return "131072";
        if (lowered.Contains("mistral-small") || lowered.Contains("codestral")) return "65536";
        if (lowered.Contains("mistral")) return "65536";
        // Qwen 通义千问
        if (lowered.Contains("qwen3.6")) return "131072";
        if (lowered.Contains("qwen-max") || lowered.Contains("qwen-plus") || lowered.Contains("qwen-coder")) return "8192";
        if (lowered.Contains("qwen")) return "8192";
        // Moonshot Kimi
        if (lowered.Contains("moonshot-v2")) return "131072";
        if (lowered.Contains("moonshot")) return "32768";
        // 智谱 GLM
        if (lowered.Contains("glm-5")) return "131072";
        if (lowered.Contains("glm-4.7")) return "65536";
        if (lowered.Contains("glm")) return "65536";
        // 百川 Baichuan
        if (lowered.Contains("baichuan5")) return "131072";
        if (lowered.Contains("baichuan4")) return "65536";
        if (lowered.Contains("baichuan")) return "65536";
        // 默认留空 (不限制)
        return "";
    }

    // ===== 初始化 =====

    private void LoadFromConfig()
    {
        var cfg = _config.GetConfig();
        if (cfg.Providers.Count > 0)
        {
            foreach (var (key, val) in cfg.Providers)
                _providers[key] = val;
        }

        var aiCfg = _config.GetAIConfig();
        if (!string.IsNullOrEmpty(aiCfg.Provider) && _providers.ContainsKey(aiCfg.Provider))
        {
            try { SetActive(aiCfg.Provider, aiCfg.Model); }
            catch { SetActive("deepseek", "deepseek-v4-pro"); }
        }
        else
        {
            SetActive("deepseek", "deepseek-v4-pro");
        }
    }

    private void EnsureBuiltInProviders()
    {
        // ===== DeepSeek =====
        if (!_providers.ContainsKey("deepseek"))
        {
            _providers["deepseek"] = new ProviderDef
            {
                Id = "deepseek",
                Name = "DeepSeek",
                BaseUrl = "https://api.deepseek.com/v1",
                Models = new List<ModelDef>
                {
                    new() { Id = "deepseek-v4-pro", Name = "DeepSeek V4 Pro", AgentMode = true, QAMode = true, Tier = ModelTier.Standard },
                    new() { Id = "deepseek-v4-flash", Name = "DeepSeek V4 Flash", AgentMode = true, QAMode = true, Tier = ModelTier.Budget },
                    new() { Id = "deepseek-r1", Name = "DeepSeek R1 (推理)", AgentMode = true, QAMode = false, Tier = ModelTier.Premium },
                }
            };
        }

        // ===== OpenAI =====
        if (!_providers.ContainsKey("openai"))
        {
            _providers["openai"] = new ProviderDef
            {
                Id = "openai",
                Name = "OpenAI",
                BaseUrl = "https://api.openai.com/v1",
                Models = new List<ModelDef>
                {
                    new() { Id = "gpt-5.2", Name = "GPT-5.2", AgentMode = true, QAMode = true, Tier = ModelTier.Premium },
                    new() { Id = "gpt-4.1", Name = "GPT-4.1", AgentMode = true, QAMode = true, Tier = ModelTier.Standard },
                    new() { Id = "gpt-4o", Name = "GPT-4o", AgentMode = true, QAMode = true, Tier = ModelTier.Budget },
                    new() { Id = "o4-mini", Name = "o4-mini (推理)", AgentMode = true, QAMode = true, Tier = ModelTier.Standard },
                }
            };
        }

        // ===== Anthropic Claude =====
        if (!_providers.ContainsKey("anthropic"))
        {
            _providers["anthropic"] = new ProviderDef
            {
                Id = "anthropic",
                Name = "Anthropic Claude",
                BaseUrl = "https://api.anthropic.com/v1",
                Models = new List<ModelDef>
                {
                    new() { Id = "claude-opus-4-7", Name = "Claude Opus 4.7", AgentMode = true, QAMode = true, Tier = ModelTier.Premium },
                    new() { Id = "claude-sonnet-4-5", Name = "Claude Sonnet 4.5", AgentMode = true, QAMode = true, Tier = ModelTier.Standard },
                    new() { Id = "claude-haiku-4-5", Name = "Claude Haiku 4.5", AgentMode = true, QAMode = true, Tier = ModelTier.Budget },
                    new() { Id = "claude-opus-4-5", Name = "Claude Opus 4.5", AgentMode = true, QAMode = true, Tier = ModelTier.Premium },
                }
            };
        }

        // ===== Google Gemini =====
        if (!_providers.ContainsKey("google"))
        {
            _providers["google"] = new ProviderDef
            {
                Id = "google",
                Name = "Google Gemini",
                BaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai",
                Models = new List<ModelDef>
                {
                    new() { Id = "gemini-3.1-pro", Name = "Gemini 3.1 Pro", AgentMode = true, QAMode = true, Tier = ModelTier.Standard },
                    new() { Id = "gemini-3.1-flash", Name = "Gemini 3.1 Flash", AgentMode = true, QAMode = true, Tier = ModelTier.Budget },
                    new() { Id = "gemini-2.5-pro", Name = "Gemini 2.5 Pro", AgentMode = true, QAMode = true, Tier = ModelTier.Standard },
                }
            };
        }

        // ===== xAI Grok =====
        if (!_providers.ContainsKey("xai"))
        {
            _providers["xai"] = new ProviderDef
            {
                Id = "xai",
                Name = "xAI Grok",
                BaseUrl = "https://api.x.ai/v1",
                Models = new List<ModelDef>
                {
                    new() { Id = "grok-4", Name = "Grok-4", AgentMode = true, QAMode = true, Tier = ModelTier.Premium },
                    new() { Id = "grok-4-mini", Name = "Grok-4 Mini", AgentMode = true, QAMode = true, Tier = ModelTier.Budget },
                }
            };
        }

        // ===== Mistral =====
        if (!_providers.ContainsKey("mistral"))
        {
            _providers["mistral"] = new ProviderDef
            {
                Id = "mistral",
                Name = "Mistral AI",
                BaseUrl = "https://api.mistral.ai/v1",
                Models = new List<ModelDef>
                {
                    new() { Id = "mistral-large", Name = "Mistral Large 2", AgentMode = true, QAMode = true, Tier = ModelTier.Standard },
                    new() { Id = "mistral-small", Name = "Mistral Small 3", AgentMode = true, QAMode = true, Tier = ModelTier.Budget },
                    new() { Id = "codestral", Name = "Codestral", AgentMode = true, QAMode = true, Tier = ModelTier.Standard },
                }
            };
        }

        // ===== Alibaba Qwen (通义千问) =====
        if (!_providers.ContainsKey("qwen"))
        {
            _providers["qwen"] = new ProviderDef
            {
                Id = "qwen",
                Name = "通义千问 (Qwen)",
                BaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1",
                Models = new List<ModelDef>
                {
                    new() { Id = "qwen3.6-35b-a3b", Name = "Qwen3.6-35B-A3B", AgentMode = true, QAMode = true, Tier = ModelTier.Standard },
                    new() { Id = "qwen-max", Name = "Qwen-Max", AgentMode = true, QAMode = true, Tier = ModelTier.Standard },
                    new() { Id = "qwen-plus", Name = "Qwen-Plus", AgentMode = true, QAMode = true, Tier = ModelTier.Budget },
                    new() { Id = "qwen-coder-plus", Name = "Qwen-Coder-Plus", AgentMode = true, QAMode = true, Tier = ModelTier.Budget },
                }
            };
        }

        // ===== Moonshot Kimi =====
        if (!_providers.ContainsKey("moonshot"))
        {
            _providers["moonshot"] = new ProviderDef
            {
                Id = "moonshot",
                Name = "Moonshot (Kimi)",
                BaseUrl = "https://api.moonshot.cn/v1",
                Models = new List<ModelDef>
                {
                    new() { Id = "moonshot-v2-128k", Name = "Kimi V2 (128K)", AgentMode = true, QAMode = true, Tier = ModelTier.Standard },
                    new() { Id = "moonshot-v1-32k", Name = "Kimi V1 (32K)", AgentMode = true, QAMode = true, Tier = ModelTier.Budget },
                }
            };
        }

        // ===== 智谱 GLM =====
        if (!_providers.ContainsKey("zhipu"))
        {
            _providers["zhipu"] = new ProviderDef
            {
                Id = "zhipu",
                Name = "智谱AI (GLM)",
                BaseUrl = "https://open.bigmodel.cn/api/paas/v4",
                Models = new List<ModelDef>
                {
                    new() { Id = "glm-5-plus", Name = "GLM-5 Plus", AgentMode = true, QAMode = true, Tier = ModelTier.Premium },
                    new() { Id = "glm-5-flash", Name = "GLM-5 Flash", AgentMode = true, QAMode = true, Tier = ModelTier.Budget },
                    new() { Id = "glm-4.7-plus", Name = "GLM-4.7 Plus", AgentMode = true, QAMode = true, Tier = ModelTier.Standard },
                }
            };
        }

        // ===== 百川 Baichuan =====
        if (!_providers.ContainsKey("baichuan"))
        {
            _providers["baichuan"] = new ProviderDef
            {
                Id = "baichuan",
                Name = "百川智能 (Baichuan)",
                BaseUrl = "https://api.baichuan-ai.com/v1",
                Models = new List<ModelDef>
                {
                    new() { Id = "baichuan5", Name = "Baichuan5", AgentMode = true, QAMode = true, Tier = ModelTier.Standard },
                    new() { Id = "baichuan4-turbo", Name = "Baichuan4 Turbo", AgentMode = true, QAMode = true, Tier = ModelTier.Budget },
                }
            };
        }
    }
}
