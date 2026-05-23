using System.Text;
using System.Text.RegularExpressions;

namespace AIIDEWPF.Services;

/// <summary>
/// 用户输入理解与矫正服务 — 本地快速纠正常见输入错误
/// 处理：打错字、字符顺序反转、标点符号错误、断句异常等
/// </summary>
public class InputCorrectionService
{
    /// <summary>被矫正的次数统计（用于决定是否提示用户）</summary>
    public int CorrectionCount { get; private set; }

    /// <summary>最近一次矫正详情</summary>
    public string? LastCorrectionDetail { get; private set; }

    /// <summary>
    /// 对用户输入进行矫正，返回矫正后的文本。
    /// 如果没有任何改动，返回原文本。
    /// </summary>
    /// <param name="input">用户原始输入</param>
    /// <param name="wasCorrected">输出参数，是否进行了矫正</param>
    /// <returns>矫正后的文本</returns>
    public string Correct(string input, out bool wasCorrected)
    {
        wasCorrected = false;
        if (string.IsNullOrWhiteSpace(input) || input.Length < 2)
            return input;

        var corrected = input.Trim();
        var changes = new List<string>();

        // 1. 全角/半角标点规范化
        var punctuationResult = NormalizePunctuation(corrected);
        if (punctuationResult.changed)
        {
            corrected = punctuationResult.text;
            changes.Add("标点规范化");
        }

        // 2. 多余空格清理（中文之间不应有多余空格，中英文之间保留一个空格）
        var spaceResult = NormalizeWhitespace(corrected);
        if (spaceResult.changed)
        {
            corrected = spaceResult.text;
            changes.Add("空格规范化");
        }

        // 3. 常见中文错别字纠正
        var typoResult = CorrectCommonTypos(corrected);
        if (typoResult.changed)
        {
            corrected = typoResult.text;
            changes.AddRange(typoResult.details);
        }

        // 4. 相邻字符顺序反转检测与纠正
        var swapResult = DetectCharacterSwaps(corrected);
        if (swapResult.changed)
        {
            corrected = swapResult.text;
            changes.AddRange(swapResult.details);
        }

        // 5. 常见英文拼写快速修复
        var engResult = CorrectCommonEnglishTypos(corrected);
        if (engResult.changed)
        {
            corrected = engResult.text;
            changes.AddRange(engResult.details);
        }

        // 6. 重复字符检测（如 "非非常常" → "非常"）
        var dupResult = FixDuplicatePhrases(corrected);
        if (dupResult.changed)
        {
            corrected = dupResult.text;
            changes.Add("重复字符修复");
        }

        if (changes.Count > 0)
        {
            wasCorrected = true;
            CorrectionCount++;
            LastCorrectionDetail = string.Join(", ", changes);
        }

        return corrected;
    }

    /// <summary>重置矫正计数</summary>
    public void Reset()
    {
        CorrectionCount = 0;
        LastCorrectionDetail = null;
    }

    // ===== 标点规范化 =====

    private static (string text, bool changed) NormalizePunctuation(string input)
    {
        var sb = new StringBuilder(input);
        bool changed = false;

        // 中文语境下：英文逗号后跟中文 → 转为中文逗号
        for (int i = 0; i < sb.Length - 1; i++)
        {
            // 英文逗号, 后紧跟中文字符 → 中文逗号，
            if (sb[i] == ',' && IsCJK(sb[i + 1]))
            {
                sb[i] = '，';
                changed = true;
            }
            // 英文句号. 后紧跟中文字符 → 中文句号。
            else if (sb[i] == '.' && IsCJK(sb[i + 1]))
            {
                sb[i] = '。';
                changed = true;
            }
            // 英文分号; 后紧跟中文字符 → 中文分号；
            else if (sb[i] == ';' && IsCJK(sb[i + 1]))
            {
                sb[i] = '；';
                changed = true;
            }
            // 英文冒号: 后紧跟中文字符 → 中文冒号：
            else if (sb[i] == ':' && IsCJK(sb[i + 1]))
            {
                sb[i] = '：';
                changed = true;
            }
            // 英文问号? 后紧跟中文字符 → 中文问号？
            else if (sb[i] == '?' && IsCJK(sb[i + 1]))
            {
                sb[i] = '？';
                changed = true;
            }
            // 英文感叹号! 后紧跟中文字符 → 中文感叹号！
            else if (sb[i] == '!' && IsCJK(sb[i + 1]))
            {
                sb[i] = '！';
                changed = true;
            }
            // 中文标点后跟英文单词 → 保留中文标点（不处理）
        }

        // 连续多个标点合并（如 "。。。" → "。"）
        for (int i = 0; i < sb.Length - 1; i++)
        {
            if (IsCNPunctuation(sb[i]) && sb[i] == sb[i + 1])
            {
                sb.Remove(i + 1, 1);
                i--;
                changed = true;
            }
        }

        return (sb.ToString(), changed);
    }

    // ===== 空格规范化 =====

    private static (string text, bool changed) NormalizeWhitespace(string input)
    {
        // 将多个连续空格合并为一个
        var result = Regex.Replace(input, @"[ ]{2,}", " ");
        bool changed = result != input;

        // 中文和中文字符之间不该有空格（除非用户可能有意为之用于分隔）
        // 这里只处理明显的多余空格：中文字符后面的空格+中文字符
        result = Regex.Replace(result, @"([\u4E00-\u9FFF\u3400-\u4DBF])\s+([\u4E00-\u9FFF\u3400-\u4DBF])", "$1$2");
        if (result != input) changed = true;

        // 中文标点后面的多余空格
        result = Regex.Replace(result, @"([，。！？；：])\s+", "$1");
        if (result != input) changed = true;

        return (result, changed);
    }

    // ===== 常见中文错别字纠正 =====

    // 常见中文输入法错别字词典（拼音联想导致）
    private static readonly Dictionary<string, string> ChineseTypoMap = new()
    {
        // 同音/近音错别字
        ["工能"] = "功能",
        ["新曾"] = "新增",
        ["支技"] = "支持",
        ["因该"] = "应该",
        ["以经"] = "已经",
        ["须要"] = "需要",
        ["设制"] = "设置",
        ["配制"] = "配置",
        ["修该"] = "修改",
        ["程续"] = "程序",
        ["模形"] = "模型",
        ["文见"] = "文件",
        ["代马"] = "代码",
        ["方发"] = "方法",
        ["雷型"] = "类型",
        ["遍量"] = "变量",
        ["函授"] = "函数",
        ["窗扣"] = "窗口",
        ["界免"] = "界面",
        ["路经"] = "路径",
        ["安健"] = "按键",
        ["令牌"] = "令牌",
        ["部属"] = "部署",
        ["汇出"] = "导出",
        ["岛入"] = "导入",
        ["缓寸"] = "缓存",
        ["管里"] = "管理",
        ["错务"] = "错误",
        ["日字"] = "日志",
        ["请球"] = "请求",
        ["相映"] = "响应",
        ["出发"] = "触发",
        ["择行"] = "执行",
        ["弟统"] = "系统",
        ["目路"] = "目录",
        ["启东"] = "启动",
        ["停制"] = "停止",
        ["链界"] = "连接",
        ["被分"] = "备份",
        ["还员"] = "还原",
        ["密月"] = "密钥",
        ["升请"] = "申请",
        ["记入"] = "接入",
        ["导行"] = "导航",
        ["填加"] = "添加",
        ["珊除"] = "删除",
        ["遍辑"] = "编辑",
        ["修定"] = "修订",
        ["仔在"] = "存在",
        ["镜项"] = "镜像",
        ["容气"] = "容器",
        ["卦闭"] = "关闭",
        ["息显"] = "显示",
        ["引藏"] = "隐藏",
        ["编立"] = "遍历",
        ["序类"] = "序列",
        ["范为"] = "范围",
        ["捡查"] = "检查",
        ["严证"] = "验证",
        ["构见"] = "构建",
        ["遍译"] = "编译",
        ["调式"] = "调试",
        ["据和"] = "聚合",
        ["窗底"] = "传递",
        ["卸载"] = "卸载",
        ["装栽"] = "装载",
        ["格试"] = "格式",
        ["解惜"] = "解析",
        ["涉及"] = "涉及",
        ["搞定"] = "搞定",
        ["按装"] = "安装",
        ["备分"] = "备份",
        ["常式"] = "尝试",

        // 常见的/地/得混淆（仅在明显语境下修正）
        ["的很好"] = "得很好",
        ["的更快"] = "得更快",
        ["的非常"] = "得非常",

        // 在/再混淆
        ["在来一次"] = "再来一次",
        ["在看一下"] = "再看一下",
        ["在说一遍"] = "再说一遍",
    };

    private static (string text, bool changed, List<string> details) CorrectCommonTypos(string input)
    {
        var result = input;
        bool changed = false;
        var details = new List<string>();

        foreach (var (wrong, correct) in ChineseTypoMap)
        {
            if (result.Contains(wrong))
            {
                result = result.Replace(wrong, correct);
                details.Add($"错别字: {wrong}→{correct}");
                changed = true;
            }
        }

        return (result, changed, details);
    }

    // ===== 相邻字符顺序反转检测 =====

    private static (string text, bool changed, List<string> details) DetectCharacterSwaps(string input)
    {
        if (input.Length < 3) return (input, false, new());

        var chars = input.ToCharArray();
        bool changed = false;
        var details = new List<string>();

        // 检测相邻中文字符的反转（只检查2-gram反转）
        // 例如 "入输法" → "输入法"，"够能" → "能够"
        var knownSwaps = new Dictionary<string, string>
        {
            ["入输"] = "输入",
            ["够能"] = "能够",
            ["择选"] = "选择",
            ["除删"] = "删除",
            ["建创"] = "创建",
            ["开打"] = "打开",
            ["存保"] = "保存",
            ["闭关"] = "关闭",
            ["启开"] = "开启",
            ["停暂"] = "暂停",
            ["续继"] = "继续",
            ["消取"] = "取消",
            ["定确"] = "确定",
            ["回返"] = "返回",
            ["新刷"] = "刷新",
            ["载下"] = "下载",
            ["传上"] = "上传",
            ["登搜"] = "搜索",
            ["换替"] = "替换",
            ["动移"] = "移动",
            ["制复"] = "复制",
            ["贴粘"] = "粘贴",
            ["除解"] = "解除",
            ["定绑"] = "绑定",
            ["载加"] = "加载",
            ["释解"] = "解释",
            ["义定"] = "定义",
            ["现实"] = "实现",
            ["作操"] = "操作",
            ["理处"] = "处理",
            ["析分"] = "分析",
            ["成生"] = "生成",
            ["化初始"] = "初始化",
            ["化优"] = "优化",
            ["整调"] = "调整",
            ["新更"] = "更新",
            ["级升"] = "升级",
            ["迁降"] = "降级",
        };

        var result = new string(chars);
        foreach (var (wrong, correct) in knownSwaps)
        {
            if (result.Contains(wrong))
            {
                result = result.Replace(wrong, correct);
                details.Add($"字符反转: {wrong}→{correct}");
                changed = true;
            }
        }

        return (result, changed, details);
    }

    // ===== 常见英文拼写修复 =====

    private static readonly Dictionary<string, string> EnglishTypoMap = new()
    {
        // 常见开发相关英文拼写错误
        ["functoin"] = "function",
        ["functon"] = "function",
        ["implment"] = "implement",
        ["implemnt"] = "implement",
        ["refactor"] = "refactor",
        ["refactr"] = "refactor",
        ["reposiory"] = "repository",
        ["repoistory"] = "repository",
        ["dependecy"] = "dependency",
        ["dependncy"] = "dependency",
        ["configration"] = "configuration",
        ["configuraton"] = "configuration",
        ["perfomance"] = "performance",
        ["preformance"] = "performance",
        ["compnent"] = "component",
        ["componet"] = "component",
        ["resposne"] = "response",
        ["repsonse"] = "response",
        ["resuest"] = "request",
        ["requet"] = "request",
        ["messge"] = "message",
        ["messgae"] = "message",
        ["excepetion"] = "exception",
        ["exceptoin"] = "exception",
        ["servcie"] = "service",
        ["serivce"] = "service",
        ["modle"] = "model",
        ["controllr"] = "controller",
        ["inteface"] = "interface",
        ["interafce"] = "interface",
        ["asynchrnous"] = "asynchronous",
        ["sychronous"] = "synchronous",
        ["initialze"] = "initialize",
        ["intialize"] = "initialize",
        ["devleop"] = "develop",
        ["develp"] = "develop",
        ["begining"] = "beginning",
        ["recieve"] = "receive",
        ["requried"] = "required",
        ["retrun"] = "return",
        ["reutrn"] = "return",
        ["buid"] = "build",
        ["debbug"] = "debug",
        ["debgu"] = "debug",
        ["strucutre"] = "structure",
        ["structue"] = "structure",
        ["architecutre"] = "architecture",
        ["architecure"] = "architecture",
        ["algorith"] = "algorithm",
        ["alogrithm"] = "algorithm",
    };

    private static (string text, bool changed, List<string> details) CorrectCommonEnglishTypos(string input)
    {
        var result = input;
        bool changed = false;
        var details = new List<string>();

        // 使用单词边界匹配，只修正整词
        foreach (var (wrong, correct) in EnglishTypoMap)
        {
            var pattern = $@"\b{Regex.Escape(wrong)}\b";
            var matches = Regex.Matches(result, pattern, RegexOptions.IgnoreCase);
            if (matches.Count > 0)
            {
                foreach (Match match in matches)
                {
                    // 保留原始大小写
                    var original = match.Value;
                    var replacement = MatchCase(correct, original);
                    result = result.Replace(original, replacement);
                    details.Add($"英文拼写: {original}→{replacement}");
                    changed = true;
                }
            }
        }

        return (result, changed, details);
    }

    /// <summary>尝试匹配原始单词的大小写风格</summary>
    private static string MatchCase(string correct, string original)
    {
        if (original.All(char.IsUpper)) return correct.ToUpper();
        if (original.All(char.IsLower)) return correct.ToLower();
        if (char.IsUpper(original[0]) && original.Skip(1).All(char.IsLower))
            return char.ToUpper(correct[0]) + correct[1..].ToLower();
        return correct.ToLower(); // 默认小写
    }

    // ===== 重复短语修复 =====

    private static (string text, bool changed) FixDuplicatePhrases(string input)
    {
        bool changed = false;

        // 检测连续重复的2字短语（中文）
        // 如 "非非" → "非" (但"常常"不应该被修正)
        var result = Regex.Replace(input, @"([\u4E00-\u9FFF])\1([\u4E00-\u9FFF])\2", m =>
        {
            // 排除合法的叠词：常常、刚刚、慢慢等
            var word = m.Groups[1].Value + m.Groups[2].Value;
            if (IsValidReduplication(m.Groups[1].Value, m.Groups[2].Value))
                return m.Value;
            changed = true;
            return word;
        });

        // 检测连续重复的4字短语 "新增新增" → "新增"
        result = Regex.Replace(result, @"([\u4E00-\u9FFF]{2,4})\1", m =>
        {
            var word = m.Groups[1].Value;
            // 排除合法的重复（如 "研究研究"、"考虑考虑"）
            if (IsValidVerbReduplication(word))
                return m.Value;
            changed = true;
            return word;
        });

        return (result, changed && result != input);
    }

    private static bool IsValidReduplication(string c1, string c2)
    {
        // 常见合法的叠词模式
        var validPatterns = new HashSet<string>
        {
            "常常", "刚刚", "渐渐", "慢慢", "悄悄", "轻轻", "往往",
            "微微", "缓缓", "默默", "偏偏", "明明", "整整", "足足",
            "连连", "频频", "通通", "统统", "仅仅", "只只",
        };
        return validPatterns.Contains(c1 + c2);
    }

    private static bool IsValidVerbReduplication(string word)
    {
        // 动词重叠如ABAB模式（研究研究、考虑考虑、讨论讨论）是合法的
        // 简单判断：如果词长度=2，且是常见动词
        var validVerbPatterns = new HashSet<string>
        {
            "研究", "考虑", "讨论", "商量", "休息", "活动", "学习",
            "练习", "修改", "调整", "检查", "清理", "整理", "了解",
            "认识", "思考", "准备", "调查", "观察", "分析", "测试",
            "参考", "比较", "尝试", "体验", "感觉", "感受", "交流",
        };
        return word.Length == 2 && validVerbPatterns.Contains(word);
    }

    // ===== 工具方法 =====

    private static bool IsCJK(char c) => CommonUtils.IsCJK(c);

    private static bool IsCNPunctuation(char c) => CommonUtils.IsCNPunctuation(c);
}
