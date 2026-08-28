using Stardrop.Models.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Stardrop.Utilities.Internal
{
    /// <summary>
    /// Reads and writes the installed collection records under Data/Collections. Kept separate from the profile
    /// files so that deleting a generated profile never destroys the pin data needed to repair a collection.
    /// </summary>
    internal static class CollectionCache
    {
        private static readonly JsonSerializerOptions _serializerOptions = new JsonSerializerOptions() { WriteIndented = true, PropertyNameCaseInsensitive = true };

        public static List<CollectionInstall> LoadAll()
        {
            List<CollectionInstall> collections = new List<CollectionInstall>();

            var cacheFolder = Pathing.GetCollectionsCacheFolderPath();
            if (Directory.Exists(cacheFolder) is false)
            {
                return collections;
            }

            foreach (var fileInfo in new DirectoryInfo(cacheFolder).GetFiles("*.json"))
            {
                try
                {
                    var collection = JsonSerializer.Deserialize<CollectionInstall>(File.ReadAllText(fileInfo.FullName), _serializerOptions);
                    if (collection is null || String.IsNullOrEmpty(collection.SourceId))
                    {
                        Program.helper.Log($"The collection record at {fileInfo.FullName} was empty or not deserializable", Helper.Status.Alert);
                        continue;
                    }

                    collections.Add(collection);
                }
                catch (Exception ex)
                {
                    Program.helper.Log($"Failed to parse the collection record at {fileInfo.FullName}: {ex}", Helper.Status.Alert);
                }
            }

            return collections;
        }

        public static CollectionInstall? Load(string sourceId)
        {
            var cachePath = Pathing.GetCollectionCachePath(sourceId);
            if (File.Exists(cachePath) is false)
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<CollectionInstall>(File.ReadAllText(cachePath), _serializerOptions);
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to parse the collection record at {cachePath}: {ex}", Helper.Status.Alert);
                return null;
            }
        }

        public static bool Save(CollectionInstall collection)
        {
            try
            {
                Directory.CreateDirectory(Pathing.GetCollectionsCacheFolderPath());
                File.WriteAllText(Pathing.GetCollectionCachePath(collection.SourceId), JsonSerializer.Serialize(collection, _serializerOptions));

                return true;
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to save the collection record for {collection.SourceId}: {ex}", Helper.Status.Alert);
                return false;
            }
        }

        /// <summary>
        /// Removes both the collection's record and its installed mods. The generated profile is left alone, as the
        /// caller decides whether to delete it or keep it as a plain profile.
        /// </summary>
        public static bool Delete(string sourceId, bool deleteInstalledMods = true)
        {
            try
            {
                var cachePath = Pathing.GetCollectionCachePath(sourceId);
                if (File.Exists(cachePath))
                {
                    File.Delete(cachePath);
                }

                if (deleteInstalledMods)
                {
                    var installPath = Pathing.GetCollectionInstallPath(sourceId);
                    if (Directory.Exists(installPath))
                    {
                        Directory.Delete(installPath, true);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to delete the collection {sourceId}: {ex}", Helper.Status.Alert);
                return false;
            }
        }
    }
}
