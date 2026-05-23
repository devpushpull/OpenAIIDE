namespace AIIDEWPF;

/// <summary>AI 交互中的魔法字符串常量</summary>
public static class AiConstants
{
    // ─── 角色名 ───
    public const string RoleUser = "user";
    public const string RoleAssistant = "assistant";
    public const string RoleSystem = "system";
    public const string RoleTool = "tool";

    // ─── 工具名 ───
    public const string ToolReadFile = "read_file";
    public const string ToolWriteFile = "write_file";
    public const string ToolCreateFile = "create_file";
    public const string ToolDeleteFile = "delete_file";
    public const string ToolRunInTerminal = "run_in_terminal";
    public const string ToolListDir = "list_dir";
    public const string ToolSearchFile = "search_file";
    public const string ToolGrepCode = "grep_code";
    public const string ToolSearchCodebase = "search_codebase";
    public const string ToolSearchWeb = "search_web";
    public const string ToolFetchContent = "fetch_content";
    public const string ToolSearchReplace = "search_replace";
    public const string ToolReadLints = "read_lints";
    public const string ToolSearchSymbol = "search_symbol";

    // ─── 工具参数字段名 ───
    public const string PropFilePath = "file_path";
    public const string PropCommand = "command";
    public const string PropQuery = "query";
    public const string PropRegex = "regex";
    public const string PropPath = "path";
    public const string PropUrl = "url";
    public const string PropContent = "content";
    public const string PropSuccess = "success";
    public const string PropError = "error";
}
