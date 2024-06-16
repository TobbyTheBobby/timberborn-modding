using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Modio;
using Modio.Filters;
using Modio.Models;
using SharpCompress.Archives;
using SharpCompress.Readers;
using ThunderKit.Core.Data;
using UnityEditor;
using File = System.IO.File;

namespace TimberbornThunderkitExtension
{
    using PV = PackageVersion;

    public class ModIoPackageSource : PackageSource
    {
        private Dictionary<string, HashSet<string>> _dependencyMap;
        private Dictionary<string, PackageGroup> _groupMap;

        private const string SettingsPath = "Assets/ThunderKitSettings";

        [InitializeOnLoadMethod]
        private static void CreateThunderKitExtensionSource() => EditorApplication.update += EnsureThunderKitExtensions;

        private static void EnsureThunderKitExtensions()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }

            EditorApplication.update -= EnsureThunderKitExtensions;

            var basePath = $"{SettingsPath}/ThunderKit Extensions.asset";
            var source = AssetDatabase.LoadAssetAtPath<ModIoPackageSource>(basePath);
            if (!source)
            {
                if (File.Exists(basePath))
                    File.Delete(basePath);
            }
        }

        [Serializable]
        public struct SDateTime
        {
            public long ticks;

            public SDateTime(long ticks)
            {
                this.ticks = ticks;
            }

            public static implicit operator DateTime(SDateTime sdt) => new(sdt.ticks);
            public static implicit operator SDateTime(DateTime sdt) => new(sdt.Ticks);
        }

        private IReadOnlyList<Mod> _mods;
        private readonly Dictionary<uint, string[]> _modDependencies = new();
        public uint GameID;
        public override string Name => "Mod.io Source";
        public override string SourceGroup => "Mod.io";

        protected override string VersionIdToGroupId(string dependencyId)
        {
            return dependencyId.Substring(0, dependencyId.LastIndexOf("-"));
        }

        protected override void OnLoadPackages()
        {
            foreach (var mod in _mods)
            {
                AddPackageGroup(new PackageGroupInfo
                {
                    Author = mod.SubmittedBy?.Username,
                    Name = mod.Name,
                    Description = mod.Summary,
                    DependencyId = $"{mod.Id}",
                    HeaderMarkdown = $"![]({mod.Logo?.Thumb320x180}){{ .icon }} {mod.Name}{{ .icon-title .header-1 }}\r\n\r\n",
                    FooterMarkdown = $"",
                    // Versions = new List<PackageVersionInfo> { new(mod.Modfile?.Version, $"{mod.Id}", _modDependencies[mod.Id], ConstructMarkdown(mod)) },
                    Versions = new List<PackageVersionInfo> { new(mod.Modfile?.Version, $"{mod.Id}", new string[] { }, ConstructMarkdown(mod)) },
                    Tags = mod.Tags.Select(tag => tag.Name).ToArray()
                });
            }

            SourceUpdated();
        }

        private static string ConstructMarkdown(Mod mod)
        {
            var markdown = $"### Description\r\n\r\n{mod.Summary}\r\n\r\n";

            if (!string.IsNullOrWhiteSpace(mod.ProfileUrl?.ToString()))
                markdown += $"{{ .links }}";

            if (!string.IsNullOrWhiteSpace(mod.ProfileUrl?.ToString())) markdown += $"[Mod.io]({mod.ProfileUrl})";

            return markdown;
        }

        protected override async void OnInstallPackageFiles(PV version, string packageDirectory)
        {
            var mod = LookupPackage(uint.Parse(version.group.DependencyId));
            var filePath = Path.Combine(packageDirectory, $"{mod.Name}.zip");

            var settings = ThunderKitSetting.GetOrCreateSettings<ModIoConfiguration>();
            var client = new Client(new Credentials(settings.ApiKey, settings.AuthToken));
            await client.Download(mod.GameId, mod.Id, mod.Modfile.Id, new FileInfo(filePath));

            using (var archive = ArchiveFactory.Open(filePath))
            {
                foreach (var entry in archive.Entries.Where(entry => entry.IsDirectory))
                {
                    var path = Path.Combine(packageDirectory, entry.Key);
                    Directory.CreateDirectory(path);
                }

                var extractOptions = new ExtractionOptions { ExtractFullPath = true, Overwrite = true };
                foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
                    entry.WriteToDirectory(packageDirectory, extractOptions);
            }

            File.Delete(filePath);
        }

        protected override async Task ReloadPagesAsyncInternal()
        {
            var settings = ThunderKitSetting.GetOrCreateSettings<ModIoConfiguration>();
            var client = new Client(new Credentials(settings.ApiKey, settings.AuthToken));
            var modsClient = client.Games[GameID].Mods;

            _mods = await modsClient.Search(ModFilter.Downloads.Desc()).FirstPage();

            foreach (var mod in _mods)
            {
                var dependencies = await client.Games[GameID].Mods[mod.Id].Dependencies.Get();
                var dependencyIds = dependencies.Select(dependency => $"{dependency.ModId}").ToArray();
                _modDependencies.Add(mod.Id, dependencyIds);
                Thread.Sleep(5);
            }

            LoadPackages();
        }

        private void PopulateDependencies()
        {
            
        }

        private Mod LookupPackage(uint modId)
        {
            return _mods.First(mod => mod.Id == modId);
        }
    }
}