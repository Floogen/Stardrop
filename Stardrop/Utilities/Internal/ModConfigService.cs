using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Json.More;
using Stardrop.Models;

namespace Stardrop.Utilities.Internal;

public class ModConfigService : IModConfigService
{
    private readonly Settings _settings;
    private readonly IModDiscoveryService _discoveryService;

    public ModConfigService(Settings settings, IModDiscoveryService discoveryService)
    {
        _settings = settings;
        _discoveryService = discoveryService;
    }

    public List<FileInfo> GetConfigFiles(DirectoryInfo modDirectory)
    {
        var configs = new List<FileInfo>();
        foreach (var directory in modDirectory.EnumerateDirectories())
        {
            var localConfigs = directory.EnumerateFiles("config.json").ToList();
            if (localConfigs.Count == 0)
            {
                configs.AddRange(GetConfigFiles(directory));
                continue;
            }

            var localConfig = localConfigs.First();
            if (localConfig.Directory is not null &&
                localConfig.Directory.EnumerateFiles("manifest.json", SearchOption.TopDirectoryOnly).Any())
            {
                configs.Add(localConfig);
            }
        }

        return configs;
    }

    public void DiscoverConfigs(string modsFilePath, IReadOnlyList<Mod> mods, bool useArchive = false)
    {
        if (!Directory.Exists(modsFilePath))
        {
            return;
        }

        foreach (var fileInfo in GetConfigFiles(new DirectoryInfo(modsFilePath)))
        {
            if (fileInfo.DirectoryName is null || 
                (Program.settings.IgnoreHiddenFolders &&
                 _discoveryService.ParentFolderContainsPeriod(modsFilePath, fileInfo.Directory)))
            {
                continue;
            }

            var mod = mods.FirstOrDefault(m => m.ModFileInfo.DirectoryName == fileInfo.DirectoryName);
            if (mod is null)
            {
                continue;
            }
            else if (useArchive && mod.Config is not null)
            {
                if (fileInfo.LastWriteTimeUtc <= mod.Config.LastWriteTimeUtc)
                {
                    continue;
                }

                mod.Config.Data = File.ReadAllText(fileInfo.FullName);
                mod.Config.LastWriteTimeUtc = fileInfo.LastWriteTimeUtc;
            }
            else
            {
                mod.Config = new Config()
                {
                    UniqueId = mod.UniqueId, FilePath = fileInfo.FullName, LastWriteTimeUtc = fileInfo.LastWriteTimeUtc,
                    Data = File.ReadAllText(fileInfo.FullName)
                };
            }
        }
    }


    public List<Config> GetPendingConfigUpdates(Profile profile, IReadOnlyList<Mod> mods,
        bool excludeMissingConfigs = false, bool useArchiveAsBase = false)
    {
        // Merge any existing preserved configs
        List<Config> pendingConfigUpdates = new List<Config>();
        foreach (var modId in profile.EnabledModIds.Select(id => id.ToLower()))
        {
            var mod = mods.FirstOrDefault(m => m.UniqueId.Equals(modId, StringComparison.OrdinalIgnoreCase));
            if (mod is null)
            {
                continue;
            }

            try
            {
                if (profile.PreservedModConfigs.ContainsKey(modId))
                {
                    // Write the archived config, if the current one doesn't exist
                    if (mod.Config is null)
                    {
                        if (excludeMissingConfigs || string.IsNullOrEmpty(mod.ModFileInfo.DirectoryName))
                        {
                            continue;
                        }

                        mod.Config = new Config()
                        {
                            UniqueId = modId, FilePath = Path.Combine(mod.ModFileInfo.DirectoryName, "config.json"),
                            Data = JsonTools.ParseDocumentToString(profile.PreservedModConfigs[modId])
                        };
                        pendingConfigUpdates.Add(mod.Config);
                    }
                    else
                    {
                        // Merge the config
                        var currentJson = mod.Config.Data;
                        var archivedJson = JsonTools.ParseDocumentToString(profile.PreservedModConfigs[modId]);
                        if (JsonDocumentEqualityComparer.Instance.Equals(JsonDocument.Parse(mod.Config.Data),
                                profile.PreservedModConfigs[modId]) is false)
                        {
                            // JsonTools.Merge will preserve the originalJson values, but will add new properties from archivedJson
                            var mergedJson = useArchiveAsBase
                                ? JsonTools.Merge(currentJson, archivedJson, false)
                                : JsonTools.Merge(archivedJson, currentJson, false);

                            // Apply the changes to the config file
                            //Program.helper.Log($"The mod {modId} does not have its current configuration preserved\nCurrent:\n{currentJson}\nArchived:\n{archivedJson}", Helper.Status.Warning);
                            pendingConfigUpdates.Add(new Config()
                                { UniqueId = modId, FilePath = mod.Config.FilePath, Data = mergedJson });
                        }
                    }
                }
                else if (mod.Config is not null)
                {
                    pendingConfigUpdates.Add(new Config()
                        { UniqueId = modId, FilePath = mod.Config.FilePath, Data = mod.Config.Data });
                }
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to process config.json for mod {modId}: {ex}", Helper.Status.Warning);
            }
        }

        return pendingConfigUpdates;
    }
}