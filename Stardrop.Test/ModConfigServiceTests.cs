using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NUnit.Framework;
using Stardrop.Models;
using Stardrop.Models.SMAPI;
using Stardrop.Utilities.Internal;

namespace Stardrop.Test;

[TestFixture]
[NonParallelizable]
public class ModConfigServiceTests
{
    private string _tempDir;
    private ModConfigService _service;
    private FakeModDiscoveryService _discoveryService;
    private Settings _savedSettings;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"stardrop-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);

        _savedSettings = Program.settings;
        Program.settings = new Settings { ModFolderPath = _tempDir, IgnoreHiddenFolders = true };

        _discoveryService = new FakeModDiscoveryService();
        _service = new ModConfigService(Program.settings, _discoveryService);
    }

    [TearDown]
    public void TearDown()
    {
        Program.settings = _savedSettings;
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    

    [Test]
    public void GetConfigFiles_EmptyDirectory_ReturnsEmpty()
    {
        var result = _service.GetConfigFiles(new DirectoryInfo(_tempDir));
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetConfigFiles_ConfigWithoutManifest_NotIncluded()
    {
        var modDir = CreateSubdir("Mod1");
        File.WriteAllText(Path.Combine(modDir, "config.json"), "{}");

        var result = _service.GetConfigFiles(new DirectoryInfo(_tempDir));

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetConfigFiles_ConfigWithManifest_Included()
    {
        var modDir = CreateSubdir("Mod1");
        var configPath = Path.Combine(modDir, "config.json");
        File.WriteAllText(configPath, "{}");
        File.WriteAllText(Path.Combine(modDir, "manifest.json"), "{}");

        var result = _service.GetConfigFiles(new DirectoryInfo(_tempDir));

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].FullName, Is.EqualTo(configPath));
    }

    [Test]
    public void GetConfigFiles_MultipleModDirs_AllIncluded()
    {
        foreach (var name in new[] { "Mod1", "Mod2", "Mod3" })
        {
            var dir = CreateSubdir(name);
            File.WriteAllText(Path.Combine(dir, "config.json"), "{}");
            File.WriteAllText(Path.Combine(dir, "manifest.json"), "{}");
        }

        var result = _service.GetConfigFiles(new DirectoryInfo(_tempDir));

        Assert.That(result, Has.Count.EqualTo(3));
    }


    

    private string CreateSubdir(string name)
    {
        var path = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private Mod CreateMod(string uniqueId, string version = "1.0.0", string name = "Test Mod", string author = "Test Author")
    {
        var modDir = CreateSubdir(uniqueId);
        var manifestPath = Path.Combine(modDir, "manifest.json");
        File.WriteAllText(manifestPath, "{}");
        var manifest = new Manifest { UniqueID = uniqueId, Name = name, Author = author, Version = version };
        return new Mod(manifest, new FileInfo(manifestPath), uniqueId, version, name, null, author);
    }

    private sealed class FakeModDiscoveryService : IModDiscoveryService
    {
        public bool ReturnValue { get; set; }

        public bool ParentFolderContainsPeriod(string oldestAncestorPath, DirectoryInfo? directoryInfo)
            => ReturnValue;
    }
}
