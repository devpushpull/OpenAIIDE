using System.Speech.Recognition;
using System.Windows;

namespace AIIDEWPF.Services;

/// <summary>
/// 语音输入服务 —— 基于 Windows Speech Recognition API，
/// 支持中文/英文语音识别，将语音转为文本插入到输入框。
/// </summary>
public class VoiceInputService : IDisposable
{
    private SpeechRecognitionEngine? _engine;
    private bool _isListening;
    private bool _isAvailable;

    /// <summary>识别到语音文本时触发</summary>
    public event Action<string>? OnSpeechRecognized;

    /// <summary>识别状态变化时触发</summary>
    public event Action<bool>? OnListeningChanged;

    /// <summary>错误发生时触发</summary>
    public event Action<string>? OnError;

    /// <summary>是否正在监听</summary>
    public bool IsListening => _isListening;

    /// <summary>语音识别是否可用</summary>
    public bool IsAvailable => _isAvailable;

    /// <summary>当前识别引擎的语言</summary>
    public string CurrentLanguage { get; private set; } = "zh-CN";

    public VoiceInputService()
    {
        Initialize();
    }

    private void Initialize()
    {
        try
        {
            // 优先尝试中文识别，失败则回退到英文
            var languages = new[] 
            { 
                new { Culture = "zh-CN", Name = "中文" },
                new { Culture = "en-US", Name = "English" } 
            };

            foreach (var lang in languages)
            {
                try
                {
                    var info = SpeechRecognitionEngine.InstalledRecognizers()
                        .FirstOrDefault(r => r.Culture.Name == lang.Culture);
                    
                    if (info != null)
                    {
                        _engine = new SpeechRecognitionEngine(info);
                        _engine.SetInputToDefaultAudioDevice();
                        _engine.LoadGrammar(new DictationGrammar());
                        _engine.SpeechRecognized += OnEngineSpeechRecognized;
                        _engine.RecognizeCompleted += OnEngineRecognizeCompleted;
                        CurrentLanguage = lang.Culture;
                        _isAvailable = true;
                        LogService.Instance.Info($"语音识别已初始化: {lang.Name} ({lang.Culture})", "Voice");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    LogService.Instance.Error(ex, "Voice");
                }
            }

            if (_engine == null)
            {
                _isAvailable = false;
                LogService.Instance.Info("未找到可用的语音识别引擎（需安装中文或英文语音包）", "Voice");
            }
        }
        catch (Exception ex)
        {
            _isAvailable = false;
            LogService.Instance.Error(ex, "Voice");
        }
    }

    /// <summary>开始监听（异步识别模式）</summary>
    public void StartListening()
    {
        if (!_isAvailable || _engine == null)
        {
            OnError?.Invoke("语音识别不可用：未检测到语音识别引擎。\n请在 Windows 设置 → 时间和语言 → 语音 中安装中文语音包。");
            return;
        }

        if (_isListening) return;

        try
        {
            _isListening = true;
            OnListeningChanged?.Invoke(true);

            // 使用异步识别，持续监听直到 Stop
            _engine.RecognizeAsync(RecognizeMode.Single);
            LogService.Instance.Info("开始语音监听", "Voice");
        }
        catch (Exception ex)
        {
            _isListening = false;
            OnListeningChanged?.Invoke(false);
            OnError?.Invoke($"启动语音识别失败: {ex.Message}");
            LogService.Instance.Error(ex, "Voice");
        }
    }

    /// <summary>停止监听</summary>
    public void StopListening()
    {
        if (!_isListening || _engine == null) return;

        try
        {
            _engine.RecognizeAsyncCancel();
            _isListening = false;
            OnListeningChanged?.Invoke(false);
            LogService.Instance.Info("停止语音监听", "Voice");
        }
        catch (Exception ex)
        {
            LogService.Instance.Error(ex, "Voice");
        }
    }

    /// <summary>切换监听状态</summary>
    public void ToggleListening()
    {
        if (_isListening)
            StopListening();
        else
            StartListening();
    }

    private void OnEngineSpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        if (e.Result?.Text == null) return;

        var text = e.Result.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        LogService.Instance.Info($"语音识别: \"{text}\" (置信度: {e.Result.Confidence:P0})", "Voice");

        // 在主线程触发事件
        Application.Current?.Dispatcher.Invoke(() =>
        {
            OnSpeechRecognized?.Invoke(text);
        });
    }

    private void OnEngineRecognizeCompleted(object? sender, RecognizeCompletedEventArgs e)
    {
        // 单次识别完成后，如果仍在监听状态，重新启动识别
        if (_isListening && _engine != null)
        {
            try
            {
                _engine.RecognizeAsync(RecognizeMode.Single);
            }
            catch
            {
                _isListening = false;
                OnListeningChanged?.Invoke(false);
            }
        }
    }

    public void Dispose()
    {
        try
        {
            if (_isListening)
                StopListening();
            _engine?.Dispose();
            _engine = null;
        }
        catch (Exception ex) { LogService.Instance.Debug($"释放语音引擎异常: {ex.Message}", "Voice"); }
    }
}
