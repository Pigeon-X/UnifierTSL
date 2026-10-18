using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using UnifierTSL.Module.Dependencies;

namespace UnifierTSL
{
    public static class ResolveHelpers
    {
        private static readonly string[] ManagedTfms = ["net9.0", "net8.0", "net7.0", "net6.0", "netstandard2.1", "netstandard2.0"];

        internal static Assembly? ResolveAssembly(AssemblyLoadContext context, AssemblyName name) {
            string[] libraryDirs = new[] {
                RuntimeLayout.BinDirectory,
                Path.Combine(RuntimeLayout.RuntimeDirectory, "app"),
                Path.Combine(RuntimeLayout.RuntimeDirectory, RuntimeLayout.LegacyDependencyDirectoryName),
                Path.Combine(RuntimeLayout.RuntimeDirectory, RuntimeLayout.LegacyLibraryDirectoryName),
                Path.Combine(AppContext.BaseDirectory, RuntimeLayout.CoreDirectoryName, RuntimeLayout.BinDirectoryName),
                Path.Combine(AppContext.BaseDirectory, RuntimeLayout.CoreDirectoryName, "app"),
                Path.Combine(AppContext.BaseDirectory, RuntimeLayout.CoreDirectoryName, RuntimeLayout.LegacyDependencyDirectoryName),
                Path.Combine(AppContext.BaseDirectory, RuntimeLayout.CoreDirectoryName, RuntimeLayout.LegacyLibraryDirectoryName),
                Path.Combine(AppContext.BaseDirectory, "app"),
                Path.Combine(AppContext.BaseDirectory, RuntimeLayout.LegacyDependencyDirectoryName),
                Path.Combine(AppContext.BaseDirectory, RuntimeLayout.LegacyLibraryDirectoryName),
            }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (string libraryDir in ManagedRuntimeDirectories().Concat(libraryDirs)) {
                string fileName = Path.Combine(libraryDir, name.Name + ".dll");
                if (!File.Exists(fileName) || IsReferenceAssembly(fileName)) {
                    continue;
                }
                try {
                    return context.LoadFromAssemblyPath(fileName);
                }
                catch (BadImageFormatException) {
                    // 遇到引用程序集或目标框架不匹配的副本时继续找运行时资产。
                }
                catch (FileLoadException) {
                    // 让 ALC 尝试其它候选路径。
                }
            }
            if (name.Name != "Terraria" && name.Name != "OTAPI" && TryResolveTerrariaEmbeddedAssembly(context, name, out Assembly? assembly)) {
                return assembly;
            }
            return null;
        }

        /// <summary>
        /// 优先枚举 managed runtime assets（runtimes/&lt;rid&gt;/lib/&lt;tfm&gt;），
        /// 再回退到旧的 bin/app/lib 目录。生产 Core 布局下这会命中
        /// Core/runtimes/win-x64/lib/netstandard2.1，而不是 Core/bin 的引用程序集。
        /// </summary>
        private static IEnumerable<string> ManagedRuntimeDirectories() {
            string currentRid = RuntimeInformation.RuntimeIdentifier;
            string[] roots = new[] {
                RuntimeLayout.RuntimeDirectory,
                AppContext.BaseDirectory,
                Directory.GetCurrentDirectory(),
            }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            foreach (string root in roots) {
                foreach (string rid in RidGraph.Instance.ExpandRuntimeIdentifier(currentRid)) {
                    foreach (string tfm in ManagedTfms) {
                        string dir = Path.Combine(root, "runtimes", rid, "lib", tfm);
                        if (seen.Add(dir)) {
                            yield return dir;
                        }
                    }
                }
            }
        }

        private static bool IsReferenceAssembly(string fileName) {
            try {
                using FileStream stream = File.OpenRead(fileName);
                using PEReader pe = new(stream);
                if (!pe.HasMetadata) return false;
                MetadataReader reader = pe.GetMetadataReader();
                AssemblyDefinition assembly = reader.GetAssemblyDefinition();
                foreach (CustomAttributeHandle handle in assembly.GetCustomAttributes()) {
                    CustomAttribute attribute = reader.GetCustomAttribute(handle);
                    EntityHandle ctor = attribute.Constructor;
                    EntityHandle parent = ctor.Kind switch {
                        HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)ctor).Parent,
                        HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)ctor).GetDeclaringType(),
                        _ => default,
                    };
                    if (parent.Kind != HandleKind.TypeReference && parent.Kind != HandleKind.TypeDefinition) {
                        continue;
                    }
                    string typeName = parent.Kind == HandleKind.TypeReference
                        ? reader.GetString(reader.GetTypeReference((TypeReferenceHandle)parent).Name)
                        : reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)parent).Name);
                    if (typeName == "ReferenceAssemblyAttribute") {
                        return true;
                    }
                }
            }
            catch {
                // 交给 LoadFromAssemblyPath 做最终校验。
            }
            return false;
        }

        private static bool TryResolveTerrariaEmbeddedAssembly(AssemblyLoadContext context, AssemblyName name, [NotNullWhen(true)] out Assembly? assembly) {
            Assembly terrariaAssembly = typeof(Terraria.Program).Assembly;
            string resourceName = name.Name + ".dll";
            string? text = Array.Find(terrariaAssembly.GetManifestResourceNames(), element => element.EndsWith(resourceName));
            if (text is not null) {
                using Stream stream = terrariaAssembly.GetManifestResourceStream(text)!;
                assembly = context.LoadFromStream(stream);
                return true;
            }
            assembly = null;
            return false;
        }

        internal static nint ResolveNativeDll(Assembly _, string unmanagedDllName) {
            string currentRid = RuntimeInformation.RuntimeIdentifier;
            IEnumerable<string> fallbackRids = RidGraph.Instance.ExpandRuntimeIdentifier(currentRid);

            string extension = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".dll" :
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? ".so" :
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? ".dylib" :
                throw new PlatformNotSupportedException(GetString("Unsupported OS platform"));

            HashSet<string> nativeNames = [];
            if (!string.IsNullOrWhiteSpace(unmanagedDllName)) {
                string original = unmanagedDllName.Trim();
                nativeNames.Add(original);

                if (!original.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) {
                    nativeNames.Add(original + extension);
                }
                else {
                    string withoutExt = Path.GetFileNameWithoutExtension(original);
                    if (!string.IsNullOrWhiteSpace(withoutExt)) {
                        nativeNames.Add(withoutExt + extension);
                    }
                }
            }

            string[] roots = new[] {
                RuntimeLayout.RuntimeDirectory,
                AppContext.BaseDirectory,
                Directory.GetCurrentDirectory(),
            }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (string root in roots) {
                foreach (string rid in fallbackRids) {
                    foreach (string nativeName in nativeNames) {
                        string currentPath = Path.Combine(root, "runtimes", rid, "native", nativeName);
                        if (File.Exists(currentPath)) {
                            return NativeLibrary.Load(currentPath);
                        }
                    }
                }
            }
            return nint.Zero;
        }
    }
}
