using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CSharp;

namespace Dynamo {
    internal static class Program {
        private const string PluginsDir = @".\plugins\";
        private const string CompiledDir = @".\compiled\";
        private const string TempDir = @".\temp\";

        private static readonly CSharpCodeProvider Compiler =
            new CSharpCodeProvider(new Dictionary<string, string> {{"CompilerVersion", "v4.0"}});

        private static readonly CompilerParameters CompilerParameters =
            new CompilerParameters(new[] {"mscorlib.dll", "System.Core.dll", Assembly.GetCallingAssembly().Location}) {
                GenerateExecutable = false,
                //CompilerOptions = "/target:library",
                GenerateInMemory = true,
                IncludeDebugInformation = false
            };

        private static readonly List<IPlugin> Plugins = new List<IPlugin>();
        private static readonly List<LoadedAssembly> Assemblies = new List<LoadedAssembly>();

        private static void Main() {
            // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
            if (!Directory.Exists(PluginsDir))
                Directory.CreateDirectory(PluginsDir);
            if (!Directory.Exists(CompiledDir))
                Directory.CreateDirectory(CompiledDir);
            if (!Directory.Exists(TempDir))
                Directory.CreateDirectory(TempDir);
            else
                Directory.GetFiles(TempDir).ForEach(File.Delete);

            var w = new FileSystemWatcher(PluginsDir, "*.cs") {
                EnableRaisingEvents = true,
                Path = PluginsDir
            };
            w.Changed += WOnChanged;
            w.Created += WOnChanged;
            w.Renamed += WOnChanged;
            w.Deleted += WOnChanged;

            // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
            Directory.GetFiles(PluginsDir, "*.cs").Select(BuildFromSource);
            Plugins.AddRange(Directory.GetFiles(CompiledDir, "*.dll").SelectMany(LoadAssembly));
            Console.WriteLine($"Loaded {Plugins.Count} plugins.");

            string line;
            Console.Write("> ");
            while (!(line = Console.ReadLine())?.Equals("exit", StringComparison.OrdinalIgnoreCase) ?? true) {
                // ReSharper disable PossibleNullReferenceException
                if (line.Equals("list", StringComparison.OrdinalIgnoreCase)) {
                    Console.WriteLine($"There are {Plugins.Count} loaded plugins:");
                    Plugins.ForEach(t => Console.WriteLine($"\t'{t.Name}' - {t.Version}"));
                }
                else if (line.Equals("help", StringComparison.OrdinalIgnoreCase)) {
                    Console.WriteLine(
                        "Available commands:\n" +
                        "\tlist    - Lists loaded plugins.\n" +
                        "\trun <p> - Runs plugin [p].\n" +
                        "\thelp    - Displays this help message.\n" +
                        "\texit    - Exits the application.");
                }
                else if (line.StartsWith("run ", StringComparison.OrdinalIgnoreCase)) {
                    var args = line.Substring(4);

                    var pl = Plugins.FirstOrDefault(p => p.Name.Equals(args, StringComparison.OrdinalIgnoreCase));
                    if (pl != null)
                        pl.Run();
                    else
                        Console.WriteLine($"Could not find plugin '{args}'. See available plugins with 'list'.");
                }
                else {
                    Console.WriteLine("Unknown command. Try 'help'..?");
                }
                // ReSharper restore PossibleNullReferenceException
                Console.Write("> ");
            }
            Console.WriteLine("Exiting...");
        }

        private static string SourcePathToDllPath(string p)
            => Path.Combine(CompiledDir, Path.GetFileNameWithoutExtension(p) + ".dll");

        private static void WOnChanged(object sender, FileSystemEventArgs fileSystemEventArgs) {
            if (new FileInfo(fileSystemEventArgs.FullPath).LastWriteTimeUtc <
                new FileInfo(Path.Combine(CompiledDir,
                    Path.GetFileNameWithoutExtension(fileSystemEventArgs.FullPath) + ".dll")).LastWriteTimeUtc)
                return;

            Console.WriteLine(
                $"\nThe following plugin source was modified on the file system and is being reloaded: {fileSystemEventArgs.Name}");
            if (!BuildFromSource(fileSystemEventArgs.FullPath)) return;
            var plugins = LoadAssembly(SourcePathToDllPath(fileSystemEventArgs.FullPath)).ToArray();
            Plugins.AddRange(plugins);
            Console.WriteLine("The following plugin(s) were loaded:");
            plugins.ForEach(p => Console.WriteLine($"\t'{p.Name}' - {p.Version}"));
            Console.Write("> ");
        }

        private static bool BuildFromSource(string fullPath) {
            var ifname = Path.GetFileNameWithoutExtension(fullPath);

            CompilerParameters.OutputAssembly = Path.Combine(TempDir, ifname + ".dll");
            var compiledPath = Path.Combine(CompiledDir, ifname + ".dll");


            if (File.Exists(CompilerParameters.OutputAssembly))
                File.Delete(CompilerParameters.OutputAssembly);

            CompilerResults results;
            string source;
            try {
                using (
                    TextReader s =
                        new StreamReader(File.Open(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))) {
                    source = s.ReadToEnd();
                    results = Compiler.CompileAssemblyFromSource(CompilerParameters, source);
                }
            }
            catch (IOException) {
                return false;
            }
            if (results.Errors.HasErrors) {
                var lines = source.Split('\n');
                Console.WriteLine($"\nThere are build errors for plugin '{ifname}':");
                results.Errors.Cast<CompilerError>()
                    .ForEach(
                        e => {
                            Console.ForegroundColor = e.IsWarning ? ConsoleColor.Yellow : ConsoleColor.Red;
                            Console.WriteLine(
                                $"\t{(e.IsWarning ? "Warning" : "Error")} {e.ErrorNumber} {e.Line}:{e.Column} -> {e.ErrorText}");
                            Console.WriteLine($"\t\t{lines[e.Line - 1]}");
                            Console.WriteLine($"\t\t{new string(' ', e.Column - 1)}^");
                        });
                Console.ResetColor();
            }
            else {
                if (results.Errors.HasWarnings) {
                    Console.WriteLine($"\nThere are build warnings for plugin '{ifname}':");
                    results.Errors.Cast<CompilerError>()
                        .ForEach(
                            e => {
                                Console.ForegroundColor = e.IsWarning ? ConsoleColor.Yellow : ConsoleColor.Red;
                                Console.WriteLine(
                                    $"\t{(e.IsWarning ? "Warning" : "Error")} {e.ErrorNumber} {e.Line}:{e.Column} -> {e.ErrorText}");
                            });
                    Console.ResetColor();
                }
                var assembly = Assemblies.FirstOrDefault(a => a.Location?.Equals(compiledPath) ?? false);
                var plugins = Plugins.Where(p => p.GetType().Assembly.Equals(assembly?.Assembly)).ToArray();
                foreach (var plugin in plugins)
                    Plugins.Remove(plugin);
                Assemblies.Remove(assembly);
                if (File.Exists(compiledPath)) File.Delete(compiledPath);
                File.Move(CompilerParameters.OutputAssembly, compiledPath);
                return true;
            }
            return false;
        }

        private static IEnumerable<IPlugin> LoadAssembly(string compiledPath) {
            Assembly na;
            Type[] enumerable;
            try {
                na = Assembly.Load(File.ReadAllBytes(compiledPath));
                enumerable = na.GetTypes().Where(t => typeof(IPlugin).IsAssignableFrom(t)).ToArray();
            }
            catch {
                Console.WriteLine($"Error: Could not load assembly {Path.GetFileName(compiledPath)}");
                yield break;
            }
            foreach (var type in enumerable)
                yield return (IPlugin) Activator.CreateInstance(type);
            if (enumerable.Length != 0)
                Assemblies.Add(new LoadedAssembly(na, compiledPath, File.GetLastWriteTimeUtc(compiledPath)));
        }

        public static void ForEach<T>(this IEnumerable<T> enumerable, Action<T> action) {
            foreach (var variable in enumerable)
                action(variable);
        }

        private class LoadedAssembly {
            public LoadedAssembly(Assembly a, string l, DateTime c) {
                Assembly = a;
                Location = l;
                CompiledTime = c;
            }

            public Assembly Assembly { get; }
            public string Location { get; }
            public DateTime CompiledTime { get; }
        }
    }
}