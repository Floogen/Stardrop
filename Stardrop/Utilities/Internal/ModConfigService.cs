using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Stardrop.Models;

namespace Stardrop.Utilities.Internal;

public class ModConfigService: IModConfigService
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
            if (fileInfo.DirectoryName is null || (Program.settings.IgnoreHiddenFolders && _discoveryService.ParentFolderContainsPeriod(modsFilePath, fileInfo.Directory)))
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
                mod.Config = new Config() { UniqueId = mod.UniqueId, FilePath = fileInfo.FullName, LastWriteTimeUtc = fileInfo.LastWriteTimeUtc, Data = File.ReadAllText(fileInfo.FullName) };
            }
        }
    }
    

}