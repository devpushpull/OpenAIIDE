namespace AIIDEWPF.Models;

/// <summary>键盘快捷键定义</summary>
public class KeyboardShortcut
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Key { get; set; } = "";
    public string Modifiers { get; set; } = "";
    public string Category { get; set; } = "通用";
}

/// <summary>快捷键配置模型</summary>
public class KeyboardShortcutsConfig
{
    public List<KeyboardShortcut> Shortcuts { get; set; } = new();

    public static KeyboardShortcutsConfig CreateDefault() => new()
    {
        Shortcuts = new List<KeyboardShortcut>
        {
            new() { Id = "file.open", Name = "打开文件", Key = "O", Modifiers = "Ctrl", Category = "文件" },
            new() { Id = "file.save", Name = "保存文件", Key = "S", Modifiers = "Ctrl", Category = "文件" },
            new() { Id = "file.new", Name = "新建文件", Key = "N", Modifiers = "Ctrl", Category = "文件" },
            new() { Id = "file.close", Name = "关闭文件", Key = "W", Modifiers = "Ctrl", Category = "文件" },
            new() { Id = "edit.inline", Name = "内联编辑", Key = "K", Modifiers = "Ctrl", Category = "编辑" },
            new() { Id = "edit.find", Name = "查找", Key = "F", Modifiers = "Ctrl", Category = "编辑" },
            new() { Id = "edit.replace", Name = "替换", Key = "H", Modifiers = "Ctrl", Category = "编辑" },
            new() { Id = "edit.complete", Name = "触发补全", Key = "P", Modifiers = "Alt", Category = "编辑" },
            new() { Id = "nav.gotoDef", Name = "跳转定义", Key = "F12", Modifiers = "", Category = "导航" },
            new() { Id = "nav.findRefs", Name = "查找引用", Key = "F12", Modifiers = "Shift", Category = "导航" },
            new() { Id = "nav.searchFile", Name = "文件搜索", Key = "P", Modifiers = "Ctrl", Category = "导航" },
            new() { Id = "nav.searchGlobal", Name = "全局搜索", Key = "F", Modifiers = "Ctrl+Shift", Category = "导航" },
            new() { Id = "debug.start", Name = "开始调试", Key = "F5", Modifiers = "", Category = "调试" },
            new() { Id = "debug.stop", Name = "停止调试", Key = "F5", Modifiers = "Shift", Category = "调试" },
            new() { Id = "debug.stepOver", Name = "单步跳过", Key = "F10", Modifiers = "", Category = "调试" },
            new() { Id = "debug.stepInto", Name = "单步进入", Key = "F11", Modifiers = "", Category = "调试" },
            new() { Id = "debug.stepOut", Name = "单步跳出", Key = "F11", Modifiers = "Shift", Category = "调试" },
            new() { Id = "debug.runToCursor", Name = "运行到光标", Key = "F10", Modifiers = "Ctrl", Category = "调试" },
            new() { Id = "debug.toggleBP", Name = "切换断点", Key = "F9", Modifiers = "", Category = "调试" },
            new() { Id = "view.terminal", Name = "切换终端", Key = "Oem3", Modifiers = "Ctrl", Category = "视图" },
            new() { Id = "view.preview", Name = "打开预览", Key = "P", Modifiers = "Ctrl+Shift", Category = "视图" },
            new() { Id = "view.aiPanel", Name = "AI面板", Key = "B", Modifiers = "Ctrl", Category = "视图" },
        }
    };
}
