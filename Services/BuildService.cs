using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AIIDEWPF.Models;

namespace AIIDEWPF.Services;

public class BuildService
{
    private string _projectPath = Environment.CurrentDirectory;

    public void SetProjectPath(string path) => _projectPath = path;

    private static readonly BuildTemplate[] Templates = GetTemplates();

    public string DetectLanguage()
    {
        if (string.IsNullOrEmpty(_projectPath)) return "未知";

        // 按优先级检测：先检查构建文件，再检查文件扩展名
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var f in Directory.GetFiles(_projectPath, "*.*", SearchOption.TopDirectoryOnly))
                files.Add(Path.GetFileName(f));
            foreach (var f in Directory.GetFiles(_projectPath, "*.*", SearchOption.AllDirectories).Take(500))
                files.Add(Path.GetFileName(f));
        }
        catch (Exception ex) { LogService.Instance.Debug($"检测语言类型异常: {ex.Message}", "Build"); }

        foreach (var t in Templates)
        {
            foreach (var bf in t.BuildFiles)
            {
                if (files.Contains(bf))
                    return t.Language;
            }
        }

        // 降级：检测文件扩展名
        var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
            exts.Add(Path.GetExtension(f).ToLowerInvariant());

        foreach (var t in Templates)
        {
            foreach (var ext in t.Extensions)
            {
                if (exts.Contains(ext))
                    return t.Language;
            }
        }

        return "未知";
    }

    public BuildTemplate? GetTemplate(string? language = null)
    {
        var lang = language ?? DetectLanguage();
        return Templates.FirstOrDefault(t => t.Language.Equals(lang, StringComparison.OrdinalIgnoreCase));
    }

    public string GetAllLanguages() => JsonSerializer.Serialize(Templates.Select(t => new
    {
        t.Language,
        BuildCommand = t.BuildCommand,
        PackageCommand = t.PackageCommand,
        BuildFiles = string.Join(", ", t.BuildFiles)
    }));

    public async Task<string> BuildAsync(string? language = null)
    {
        var tpl = GetTemplate(language);
        if (tpl == null)
            return "{\"success\":false,\"error\":\"未检测到支持的编程语言，或项目不包含可识别的构建文件。\"}";

        if (string.IsNullOrEmpty(tpl.BuildCommand))
            return $"{{\"success\":false,\"error\":\"{tpl.Language} 暂不支持一键编译，请使用终端手动执行。\"}}";

        LogService.Instance.Info($"开始编译: {tpl.Language} | {tpl.BuildCommand}", "Build");
        return await RunCommandAsync(tpl.BuildCommand, _projectPath);
    }

    /// <summary>综合验证：自动检测语言并编译，返回带语言信息的统一报告</summary>
    public async Task<string> VerifyAsync()
    {
        var language = DetectLanguage();
        if (language == "未知")
            return "{\"success\":false,\"error\":\"未检测到支持的编程语言。请确认项目包含可识别的构建文件。\"}";

        LogService.Instance.Info($"开始验证: 检测到语言={language}", "Build");
        var buildResult = await BuildAsync(language);

        try
        {
            var buildJson = JsonSerializer.Deserialize<JsonElement>(buildResult);
            var buildSuccess = buildJson.TryGetProperty("success", out var s) && s.GetBoolean();
            var buildOutput = buildJson.TryGetProperty("output", out var o) ? o.GetString() ?? "" : "";
            var buildError = buildJson.TryGetProperty("stderr", out var e) ? e.GetString() ?? "" : "";
            var exitCode = buildJson.TryGetProperty("exitCode", out var ec) ? ec.GetInt32() : -1;

            return JsonSerializer.Serialize(new
            {
                success = buildSuccess,
                language,
                detectedLanguage = language,
                verifyAction = "build",
                buildResult = new
                {
                    passed = buildSuccess,
                    exitCode,
                    output = Truncate(buildOutput, 5000),
                    stderr = Truncate(buildError, 3000)
                },
                message = buildSuccess
                    ? $"{language} 项目编译验证通过 (exit={exitCode})"
                    : $"{language} 项目编译失败 (exit={exitCode})，请检查错误信息并修复"
            });
        }
        catch
        {
            return buildResult;
        }
    }

    public async Task<string> PackageAsync(string? language = null)
    {
        var tpl = GetTemplate(language);
        if (tpl == null)
            return "{\"success\":false,\"error\":\"未检测到支持的编程语言。\"}";

        var cmd = !string.IsNullOrEmpty(tpl.PackageCommand) ? tpl.PackageCommand : tpl.BuildCommand;
        if (string.IsNullOrEmpty(cmd))
            return $"{{\"success\":false,\"error\":\"{tpl.Language} 暂不支持打包。\"}}";

        LogService.Instance.Info($"开始打包: {tpl.Language} | {cmd}", "Build");
        return await RunCommandAsync(cmd, _projectPath);
    }

    private static async Task<string> RunCommandAsync(string command, string cwd)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                WorkingDirectory = cwd,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            var p = Process.Start(psi);
            if (p == null) return "{\"success\":false,\"error\":\"无法启动编译进程\"}";

            var stdout = await p.StandardOutput.ReadToEndAsync();
            var stderr = await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();

            if (p.ExitCode != 0)
                LogService.Instance.Warn($"编译失败 (exit={p.ExitCode}): {Truncate(stderr, 200)}", "Build");
            else
                LogService.Instance.Info("编译成功 (exit=0)", "Build");

            // 写入 build_output 日志文件，供排查问题使用
            WriteBuildOutputFile(command, cwd, p.ExitCode, stdout, stderr);

            return JsonSerializer.Serialize(new
            {
                success = p.ExitCode == 0,
                command,
                exitCode = p.ExitCode,
                output = Truncate(stdout, 10000),
                stderr = Truncate(stderr, 5000)
            });
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"编译异常: {ex.Message}", "Build");
            return $"{{\"success\":false,\"error\":\"{ex.Message.Replace("\"", "\\\"")}\"}}";
        }
    }

    private static string Truncate(string s, int max) => CommonUtils.Truncate(s, max);

    /// <summary>将构建输出写入日志文件，供离线排查</summary>
    private static void WriteBuildOutputFile(string command, string cwd, int exitCode, string stdout, string stderr)
    {
        try
        {
            var logsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            Directory.CreateDirectory(logsDir);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var filePath = Path.Combine(logsDir, $"build_{timestamp}.log");

            var content = new System.Text.StringBuilder();
            content.AppendLine($"=== Build Output ===");
            content.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            content.AppendLine($"Command: {command}");
            content.AppendLine($"WorkingDir: {cwd}");
            content.AppendLine($"ExitCode: {exitCode}");
            content.AppendLine($"Status: {(exitCode == 0 ? "SUCCESS" : "FAILED")}");
            content.AppendLine();
            content.AppendLine("=== STDOUT ===");
            content.AppendLine(stdout);
            if (!string.IsNullOrEmpty(stderr))
            {
                content.AppendLine();
                content.AppendLine("=== STDERR ===");
                content.AppendLine(stderr);
            }
            content.AppendLine();
            content.AppendLine("=== END ===");

            File.WriteAllText(filePath, content.ToString());
            LogService.Instance.Info($"构建日志已写入: {filePath}", "Build");
        }
        catch (Exception ex)
        {
            LogService.Instance.Warn($"写入构建日志失败（不影响主流程）: {ex.Message}", "Build");
        }
    }

    // ===== 50 种语言编译模板 =====
    private static BuildTemplate[] GetTemplates() => new BuildTemplate[]
    {
        new() { Language="C#",      Extensions=new[]{".cs"},         BuildFiles=new[]{"*.csproj","*.sln"},               BuildCommand="dotnet build",              PackageCommand="dotnet publish -c Release",       RunCommand="dotnet run" },
        new() { Language="Java",    Extensions=new[]{".java"},       BuildFiles=new[]{"pom.xml","build.gradle","build.gradle.kts"}, BuildCommand="mvn compile -q",         PackageCommand="mvn package -DskipTests",          RunCommand="mvn exec:java" },
        new() { Language="Python",  Extensions=new[]{".py"},         BuildFiles=new[]{"requirements.txt","setup.py","pyproject.toml"}, BuildCommand="python -m compileall .",  PackageCommand="python setup.py sdist",             RunCommand="python ." },
        new() { Language="JavaScript", Extensions=new[]{".js"},      BuildFiles=new[]{"package.json"},                     BuildCommand="npm run build",             PackageCommand="npm pack",                          RunCommand="node ." },
        new() { Language="TypeScript",Extensions=new[]{".ts",".tsx"},BuildFiles=new[]{"tsconfig.json"},                    BuildCommand="npx tsc --noEmit",          PackageCommand="npm run build",                     RunCommand="npx ts-node ." },
        new() { Language="Go",      Extensions=new[]{".go"},         BuildFiles=new[]{"go.mod"},                           BuildCommand="go build ./...",            PackageCommand="go build -o app .",                 RunCommand="go run ." },
        new() { Language="Rust",    Extensions=new[]{".rs"},         BuildFiles=new[]{"Cargo.toml"},                       BuildCommand="cargo build",               PackageCommand="cargo build --release",             RunCommand="cargo run" },
        new() { Language="C",       Extensions=new[]{".c",".h"},     BuildFiles=new[]{"Makefile","CMakeLists.txt"},        BuildCommand="make",                      PackageCommand="make release",                      RunCommand="make run" },
        new() { Language="C++",     Extensions=new[]{".cpp",".cc",".cxx",".hpp"}, BuildFiles=new[]{"CMakeLists.txt","Makefile"},    BuildCommand="cmake --build build",       PackageCommand="cmake --build build --config Release", RunCommand="cmake --build build && ./app" },
        new() { Language="Swift",   Extensions=new[]{".swift"},      BuildFiles=new[]{"Package.swift"},                    BuildCommand="swift build",               PackageCommand="swift build -c release",            RunCommand="swift run" },
        new() { Language="Kotlin",  Extensions=new[]{".kt",".kts"},  BuildFiles=new[]{"build.gradle.kts","build.gradle"},  BuildCommand="gradle build",              PackageCommand="gradle jar",                        RunCommand="gradle run" },
        new() { Language="Dart",    Extensions=new[]{".dart"},       BuildFiles=new[]{"pubspec.yaml"},                     BuildCommand="dart compile .",            PackageCommand="dart compile exe .",                RunCommand="dart run ." },
        new() { Language="PHP",     Extensions=new[]{".php"},        BuildFiles=new[]{"composer.json"},                    BuildCommand="php -l .",                  PackageCommand="composer archive",                  RunCommand="php -S localhost:8000" },
        new() { Language="Ruby",    Extensions=new[]{".rb"},         BuildFiles=new[]{"Gemfile"},                          BuildCommand="ruby -c .",                 PackageCommand="gem build *.gemspec",               RunCommand="ruby ." },
        new() { Language="Perl",    Extensions=new[]{".pl",".pm"},   BuildFiles=new[]{"Makefile.PL","Build.PL"},           BuildCommand="perl -c .",                 PackageCommand="perl Makefile.PL && make",          RunCommand="perl ." },
        new() { Language="Lua",     Extensions=new[]{".lua"},        BuildFiles=new[]{"*.rockspec"},                       BuildCommand="luac -p .",                 PackageCommand="luarocks pack",                     RunCommand="lua ." },
        new() { Language="R",       Extensions=new[]{".r",".R"},     BuildFiles=new[]{"DESCRIPTION"},                      BuildCommand="R CMD check .",             PackageCommand="R CMD build .",                     RunCommand="Rscript ." },
        new() { Language="Scala",   Extensions=new[]{".scala"},      BuildFiles=new[]{"build.sbt"},                        BuildCommand="sbt compile",               PackageCommand="sbt assembly",                      RunCommand="sbt run" },
        new() { Language="Haskell", Extensions=new[]{".hs"},         BuildFiles=new[]{"*.cabal","stack.yaml"},             BuildCommand="stack build",               PackageCommand="stack install",                     RunCommand="stack run" },
        new() { Language="Erlang",  Extensions=new[]{".erl"},        BuildFiles=new[]{"rebar.config"},                     BuildCommand="rebar3 compile",            PackageCommand="rebar3 release",                    RunCommand="rebar3 shell" },
        new() { Language="Elixir",  Extensions=new[]{".ex",".exs"},  BuildFiles=new[]{"mix.exs"},                          BuildCommand="mix compile",               PackageCommand="mix release",                       RunCommand="mix run" },
        new() { Language="Clojure", Extensions=new[]{".clj"},        BuildFiles=new[]{"project.clj","deps.edn"},           BuildCommand="lein compile",              PackageCommand="lein uberjar",                      RunCommand="lein run" },
        new() { Language="F#",      Extensions=new[]{".fs",".fsx"},  BuildFiles=new[]{"*.fsproj"},                         BuildCommand="dotnet build",              PackageCommand="dotnet publish -c Release",         RunCommand="dotnet run" },
        new() { Language="VB.NET",  Extensions=new[]{".vb"},         BuildFiles=new[]{"*.vbproj"},                         BuildCommand="dotnet build",              PackageCommand="dotnet publish -c Release",         RunCommand="dotnet run" },
        new() { Language="SQL",     Extensions=new[]{".sql"},        BuildFiles=Array.Empty<string>(),                   BuildCommand="",                          PackageCommand="",                                  RunCommand="" },
        new() { Language="Shell",   Extensions=new[]{".sh"},         BuildFiles=Array.Empty<string>(),                   BuildCommand="bash -n .",                 PackageCommand="",                                  RunCommand="bash ." },
        new() { Language="PowerShell", Extensions=new[]{".ps1"},     BuildFiles=Array.Empty<string>(),                   BuildCommand="powershell -NoProfile -Command Get-Command", PackageCommand="",                    RunCommand="powershell -File ." },
        new() { Language="MATLAB",  Extensions=new[]{".m"},          BuildFiles=Array.Empty<string>(),                   BuildCommand="",                          PackageCommand="",                                  RunCommand="" },
        new() { Language="Julia",   Extensions=new[]{".jl"},         BuildFiles=new[]{"Project.toml"},                     BuildCommand="julia -e 'using Pkg; Pkg.instantiate()'", PackageCommand="",                      RunCommand="julia ." },
        new() { Language="Groovy",  Extensions=new[]{".groovy"},     BuildFiles=new[]{"build.gradle"},                     BuildCommand="gradle build",              PackageCommand="gradle jar",                        RunCommand="gradle run" },
        new() { Language="Objective-C", Extensions=new[]{".m",".mm"},BuildFiles=new[]{"Makefile","*.xcodeproj"},           BuildCommand="make",                      PackageCommand="xcodebuild archive",                RunCommand="make run" },
        new() { Language="Assembly",Extensions=new[]{".asm",".s"},   BuildFiles=new[]{"Makefile"},                         BuildCommand="make",                      PackageCommand="",                                  RunCommand="make run" },
        new() { Language="V",       Extensions=new[]{".v"},          BuildFiles=new[]{"v.mod"},                            BuildCommand="v .",                       PackageCommand="v -prod .",                         RunCommand="v run ." },
        new() { Language="Zig",     Extensions=new[]{".zig"},        BuildFiles=new[]{"build.zig"},                        BuildCommand="zig build",                 PackageCommand="zig build -Doptimize=ReleaseFast",  RunCommand="zig run ." },
        new() { Language="Nim",     Extensions=new[]{".nim"},        BuildFiles=new[]{"*.nimble"},                         BuildCommand="nim c .",                   PackageCommand="nim c -d:release .",                RunCommand="nim r ." },
        new() { Language="Crystal", Extensions=new[]{".cr"},         BuildFiles=new[]{"shard.yml"},                        BuildCommand="crystal build .",           PackageCommand="crystal build --release .",         RunCommand="crystal run ." },
        new() { Language="OCaml",   Extensions=new[]{".ml",".mli"},  BuildFiles=new[]{"dune-project"},                     BuildCommand="dune build",                PackageCommand="dune build --release",              RunCommand="dune exec ." },
        new() { Language="Reason",  Extensions=new[]{".re"},         BuildFiles=new[]{"bsconfig.json"},                    BuildCommand="bsb -make-world",           PackageCommand="npm run build",                     RunCommand="node ." },
        new() { Language="PureScript", Extensions=new[]{".purs"},    BuildFiles=new[]{"spago.dhall"},                      BuildCommand="spago build",               PackageCommand="spago bundle-app",                  RunCommand="spago run" },
        new() { Language="Elm",     Extensions=new[]{".elm"},        BuildFiles=new[]{"elm.json"},                         BuildCommand="elm make .",                PackageCommand="elm make --optimize .",             RunCommand="" },
        new() { Language="Haxe",    Extensions=new[]{".hx"},         BuildFiles=new[]{"*.hxml"},                           BuildCommand="haxe build.hxml",           PackageCommand="haxe build.hxml -D release",        RunCommand="haxe run.hxml" },
        new() { Language="Vala",    Extensions=new[]{".vala"},       BuildFiles=new[]{"meson.build"},                      BuildCommand="meson build",               PackageCommand="ninja -C build",                    RunCommand="meson run" },
        new() { Language="Ada",     Extensions=new[]{".adb",".ads"}, BuildFiles=new[]{"*.gpr"},                            BuildCommand="gprbuild",                  PackageCommand="gprbuild -P*.gpr",                  RunCommand="" },
        new() { Language="Fortran", Extensions=new[]{".f90",".f",".f95"}, BuildFiles=new[]{"Makefile","CMakeLists.txt"},     BuildCommand="make",                      PackageCommand="make release",                      RunCommand="make run" },
        new() { Language="COBOL",   Extensions=new[]{".cbl",".cob"}, BuildFiles=Array.Empty<string>(),                   BuildCommand="cobc -x .",                 PackageCommand="",                                  RunCommand="" },
        new() { Language="Pascal",  Extensions=new[]{".pas"},        BuildFiles=new[]{"*.lpr"},                            BuildCommand="fpc .",                     PackageCommand="fpc -O2 .",                         RunCommand="" },
        new() { Language="D",       Extensions=new[]{".d"},          BuildFiles=new[]{"dub.json","dub.sdl"},              BuildCommand="dub build",                 PackageCommand="dub build -b release",              RunCommand="dub run" },
        new() { Language="Scheme",  Extensions=new[]{".scm",".ss"},  BuildFiles=Array.Empty<string>(),                   BuildCommand="",                          PackageCommand="",                                  RunCommand="" },
        new() { Language="Racket",  Extensions=new[]{".rkt"},        BuildFiles=Array.Empty<string>(),                   BuildCommand="raco make .",               PackageCommand="raco exe .",                        RunCommand="racket ." },
        new() { Language="Tcl",     Extensions=new[]{".tcl"},        BuildFiles=Array.Empty<string>(),                   BuildCommand="",                          PackageCommand="",                                  RunCommand="tclsh ." }
    };
}
